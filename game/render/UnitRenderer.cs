using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>Creates 3D unit views from sim state. Reads only; map X/Y maps to world X/Z.</summary>
public partial class UnitRenderer : Node3D
{
    public UnitStore Units = null!;
    public Func<IReadOnlyCollection<int>>? SelectedIds;
    public Func<int, Vector3?>? TargetPos;

    private const string InfantryScenePath = "res://assets/rifleman/rifleman.glb";
    private const float PickableUnitHeight = 0.9f;
    private readonly Dictionary<int, Vector3>[] _snapshots = { new(), new() };
    private readonly Dictionary<int, UnitView> _views = new();
    private readonly Dictionary<int, int> _lastCooldown = new();
    private PackedScene? _infantryScene;
    private ImmediateMesh _targetLineMesh = null!;
    private int _lastTick = -1;

    private sealed class UnitView
    {
        public required Node3D Root { get; init; }
        public required MeshInstance3D SelectionRing { get; init; }
        public required MeshInstance3D HealthBackground { get; init; }
        public required MeshInstance3D HealthFill { get; init; }
        public AnimationPlayer? Animator { get; init; }
    }

    public static Vector3 ToWorld(Fix64 mapX, Fix64 mapY) =>
        new((float)mapX.ToDouble(), 0, (float)mapY.ToDouble());

    public override void _Ready()
    {
        _infantryScene = ResourceLoader.Load<PackedScene>(InfantryScenePath);
        if (_infantryScene == null)
            GD.PushWarning($"Could not load {InfantryScenePath}; using a capsule proxy until the unit asset is installed.");

        var lineMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.0f, 0.18f, 0.12f, 0.72f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _targetLineMesh = new ImmediateMesh();
        AddChild(new MeshInstance3D { Mesh = _targetLineMesh, MaterialOverride = lineMaterial });
    }

    /// <summary>Capture one completed fixed-step snapshot for render interpolation.</summary>
    public void CaptureSnapshot(int tick)
    {
        var buffer = _snapshots[tick % 2];
        buffer.Clear();
        foreach (var unit in Units.Units)
            buffer[unit.Id.Value] = ToWorld(unit.Position.X, unit.Position.Y);
        _lastTick = tick;
    }

    public Vector2 ScreenPosition(Vector2 screenPoint, RtsCamera camera)
    {
        var ground = camera.GroundPointAt(screenPoint);
        return ground.HasValue ? new Vector2(ground.Value.X, ground.Value.Z) : Vector2.Zero;
    }

    public override void _Process(double delta)
    {
        if (Units == null || _lastTick < 0) return;
        var blend = (float)Main.DrawAlpha;
        var previous = _snapshots[(_lastTick + 1) % 2];
        var current = _snapshots[_lastTick % 2];
        var selected = SelectedIds?.Invoke();
        var live = Units.Units.Select(u => u.Id.Value).ToHashSet();

        foreach (var staleId in _views.Keys.Where(id => !live.Contains(id)).ToArray())
        {
            _views[staleId].Root.QueueFree();
            _views.Remove(staleId);
            _lastCooldown.Remove(staleId);
        }

        _targetLineMesh.ClearSurfaces();
        var lineCount = 0;
        foreach (var unit in Units.Units)
        {
            var id = unit.Id.Value;
            if (!current.TryGetValue(id, out var position)) continue;
            if (!_views.TryGetValue(id, out var view))
            {
                view = CreateView(unit);
                _views.Add(id, view);
                if (previous.TryGetValue(id, out var firstPosition)) position = firstPosition;
            }

            if (previous.TryGetValue(id, out var oldPosition))
                position = oldPosition.Lerp(position, blend);
            view.Root.Position = position;

            var vx = (float)unit.Velocity.X.ToDouble();
            var vy = (float)unit.Velocity.Y.ToDouble();
            var moving = vx * vx + vy * vy > 0.0001f;
            if (moving)
                view.Root.Rotation = new Vector3(0, Mathf.Atan2(-vx, -vy), 0);
            UpdateAnimation(view.Animator, unit, moving);

            view.SelectionRing.Visible = selected?.Contains(id) == true;
            var hpFraction = unit.MaxHp.Raw <= 0 ? 1.0f : Mathf.Clamp((float)(unit.Hp.ToDouble() / unit.MaxHp.ToDouble()), 0.0f, 1.0f);
            view.HealthBackground.Visible = hpFraction < 0.999f;
            view.HealthFill.Visible = hpFraction < 0.999f;
            view.HealthFill.Scale = new Vector3(hpFraction, 1, 1);
            view.HealthFill.Position = new Vector3(-0.35f * (1.0f - hpFraction), 1.95f, 0);

            if (unit.TargetId.Value >= 0 && TargetPos?.Invoke(unit.TargetId.Value) is { } target)
            {
                if (lineCount == 0) _targetLineMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
                _targetLineMesh.SurfaceAddVertex(position + Vector3.Up * 0.12f);
                _targetLineMesh.SurfaceAddVertex(target + Vector3.Up * 0.12f);
                lineCount++;
            }
        }
        if (lineCount > 0) _targetLineMesh.SurfaceEnd();
    }

    private UnitView CreateView(Unit unit)
    {
        var root = new Node3D { Name = $"Unit_{unit.Id.Value}" };
        AddChild(root);
        Node3D? model = _infantryScene?.Instantiate<Node3D>();
        if (model == null)
        {
            var fallbackMaterial = new StandardMaterial3D
            {
                AlbedoColor = unit.Faction == 0 ? new Color(0.2f, 0.52f, 0.78f) : new Color(0.72f, 0.25f, 0.2f),
                Roughness = 0.8f,
            };
            model = new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = 0.28f, Height = 1.35f },
                MaterialOverride = fallbackMaterial,
                Position = new Vector3(0, 0.68f, 0),
            };
        }
        root.AddChild(model);

        var factionColor = unit.Faction == 0 ? new Color(0.18f, 0.78f, 1.0f) : new Color(1.0f, 0.28f, 0.2f);
        var ringMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = factionColor,
        };
        var ring = new MeshInstance3D
        {
            Name = "SelectionRing",
            Mesh = new TorusMesh { InnerRadius = 0.32f, OuterRadius = 0.38f, Rings = 32, RingSegments = 6 },
            MaterialOverride = ringMaterial,
            Position = new Vector3(0, 0.035f, 0),
            Visible = false,
        };
        root.AddChild(ring);

        var background = MakeHealthBar(new Color(0.08f, 0.07f, 0.06f), new Vector3(0, 1.95f, 0));
        var fill = MakeHealthBar(new Color(0.2f, 0.9f, 0.28f), new Vector3(0, 1.95f, 0));
        root.AddChild(background);
        root.AddChild(fill);

        return new UnitView
        {
            Root = root,
            SelectionRing = ring,
            HealthBackground = background,
            HealthFill = fill,
            Animator = FindAnimationPlayer(model),
        };
    }

    private static MeshInstance3D MakeHealthBar(Color color, Vector3 position) => new()
    {
        Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.055f, 0.035f) },
        MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = color },
        Position = position,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    private void UpdateAnimation(AnimationPlayer? animator, Unit unit, bool moving)
    {
        if (animator == null) return;
        var oldCooldown = _lastCooldown.GetValueOrDefault(unit.Id.Value, 0);
        var fired = unit.TargetId != EntityId.None && oldCooldown <= 0 && unit.CooldownRemaining > 0 && HasClip(animator, "fire");
        _lastCooldown[unit.Id.Value] = unit.CooldownRemaining;

        if (fired)
        {
            animator.Play("fire");
            return;
        }
        if (animator.IsPlaying() && animator.CurrentAnimation == "fire") return;
        var locomotion = moving ? "move" : "idle";
        if (HasClip(animator, locomotion) && (!animator.IsPlaying() || animator.CurrentAnimation != locomotion))
            animator.Play(locomotion);
    }

    private static bool HasClip(AnimationPlayer player, string name) =>
        player.GetAnimationList().Contains(name);

    private static AnimationPlayer? FindAnimationPlayer(Node node)
    {
        if (node is AnimationPlayer player) return player;
        foreach (var child in node.GetChildren())
            if (FindAnimationPlayer(child) is { } found) return found;
        return null;
    }
}
