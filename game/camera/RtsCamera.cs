using Godot;

namespace Rts.Game;

/// <summary>Pan/zoom RTS camera. Arrow keys + edge-scroll pan; wheel zooms anchored at cursor.
/// WASD is deliberately NOT bound — those letters are order hotkeys (A attack-move, S stop).</summary>
public partial class RtsCamera : Camera2D
{
    public const float MinZoom = 0.5f;
    public const float MaxZoom = 4.0f;
    private const float PanSpeed = 900.0f; // world px/s at zoom 1
    private const float ZoomStep = 1.12f;
    private const float Edge = 8.0f;

    public override void _Process(double delta)
    {
        var d = (float)delta;

        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.Right)) dir.X += 1;
        if (Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.Down)) dir.Y += 1;

        var vp = GetViewport().GetVisibleRect().Size;
        var s = GetViewport().GetMousePosition();
        if (s.X < Edge) dir.X -= 1;
        else if (s.X > vp.X - Edge) dir.X += 1;
        if (s.Y < Edge) dir.Y -= 1;
        else if (s.Y > vp.Y - Edge) dir.Y += 1;

        if (dir != Vector2.Zero)
            Position += dir.Normalized() * PanSpeed * d / Zoom.X;
    }

    public override void _UnhandledInput(Godot.InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mb) return;
        var factor = mb.ButtonIndex switch
        {
            MouseButton.WheelUp => ZoomStep,
            MouseButton.WheelDown => 1.0f / ZoomStep,
            _ => 0f,
        };
        if (factor == 0f) return;

        var anchor = GetGlobalMousePosition();
        var z = Mathf.Clamp(Zoom.X * factor, MinZoom, MaxZoom);
        if (Mathf.IsEqualApprox(z, Zoom.X)) return;
        // keep the world point under the cursor fixed: pos' = mp − (mp − pos) · (zoom/z)
        Position = anchor - (anchor - Position) * (Zoom.X / z);
        Zoom = new Vector2(z, z);
    }
}
