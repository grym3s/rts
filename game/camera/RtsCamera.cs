using Godot;

namespace Rts.Game;

/// <summary>High-angle 3D RTS camera. Map north is -Z; the camera keeps a fixed battlefield pitch.</summary>
public partial class RtsCamera : Camera3D
{
    private const float PitchDegrees = 52.0f;
    private const float DefaultDistance = 92.0f;
    private const float MinDistance = 18.0f;
    private const float MaxDistance = 112.0f;
    private const float ZoomStep = 1.12f;
    private const float PanSpeed = 24.0f;
    private const float Edge = 8.0f;

    private Vector3 _target;
    private Vector2 _mapSize = new(48, 32);
    private float _distance = DefaultDistance;

    public void Configure(Vector3 target, Vector2 mapSize)
    {
        _target = new Vector3(target.X, 0, target.Z);
        _mapSize = mapSize;
        Projection = ProjectionType.Perspective;
        Fov = 45.0f;
        Near = 0.05f;
        Far = 300.0f;
        _distance = DefaultDistance;
        PlaceCamera();
    }

    /// <summary>Recentres the camera rig on a world point (used by scripted shots).</summary>
    public void FocusOn(Vector3 world)
    {
        _target = new Vector3(world.X, 0, world.Z);
        ClampTarget();
        PlaceCamera();
    }

    /// <summary>Sets the orbit distance (clamped to zoom limits); used by the shot choreography.</summary>
    public float FocusDistance
    {
        get => _distance;
        set
        {
            _distance = Mathf.Clamp(value, MinDistance, MaxDistance);
            PlaceCamera();
        }
    }

    /// <summary>Returns the world position where a screen ray meets the simulation ground plane.</summary>
    public Vector3? GroundPointAt(Vector2 screen)
    {
        var origin = ProjectRayOrigin(screen);
        var direction = ProjectRayNormal(screen);
        if (Mathf.Abs(direction.Y) < 0.0001f) return null;
        var distance = -origin.Y / direction.Y;
        return distance < 0 ? null : origin + direction * distance;
    }

    public override void _Process(double delta)
    {
        var direction = Vector2.Zero;
        if (Input.IsKeyPressed(Key.Left)) direction.X -= 1;
        if (Input.IsKeyPressed(Key.Right)) direction.X += 1;
        if (Input.IsKeyPressed(Key.Up)) direction.Y -= 1;
        if (Input.IsKeyPressed(Key.Down)) direction.Y += 1;

        var viewport = GetViewport().GetVisibleRect().Size;
        var mouse = GetViewport().GetMousePosition();
        if (mouse.X < Edge) direction.X -= 1;
        else if (mouse.X > viewport.X - Edge) direction.X += 1;
        if (mouse.Y < Edge) direction.Y -= 1;
        else if (mouse.Y > viewport.Y - Edge) direction.Y += 1;

        if (direction != Vector2.Zero)
        {
            var scale = _distance / DefaultDistance;
            _target += new Vector3(direction.X, 0, direction.Y).Normalized() * PanSpeed * scale * (float)delta;
            ClampTarget();
            PlaceCamera();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } button) return;
        var factor = button.ButtonIndex switch
        {
            MouseButton.WheelUp => 1.0f / ZoomStep,
            MouseButton.WheelDown => ZoomStep,
            _ => 1.0f,
        };
        if (Mathf.IsEqualApprox(factor, 1.0f)) return;

        var before = GroundPointAt(button.Position);
        _distance = Mathf.Clamp(_distance * factor, MinDistance, MaxDistance);
        PlaceCamera();
        var after = GroundPointAt(button.Position);
        if (before.HasValue && after.HasValue)
        {
            _target += before.Value - after.Value;
            _target.Y = 0;
            ClampTarget();
            PlaceCamera();
        }
    }

    private void PlaceCamera()
    {
        var pitch = Mathf.DegToRad(PitchDegrees);
        var offset = new Vector3(0, Mathf.Sin(pitch), Mathf.Cos(pitch)) * _distance;
        GlobalPosition = _target + offset;
        LookAt(_target, Vector3.Up);
    }

    private void ClampTarget()
    {
        _target.X = Mathf.Clamp(_target.X, -2, _mapSize.X + 2);
        _target.Z = Mathf.Clamp(_target.Z, -2, _mapSize.Y + 2);
    }
}
