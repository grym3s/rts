using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Rts.Sim.Core;
using Rts.Sim.Combat;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>Root scene: owns the SimWorld, steps it at a fixed rate, drives render/overlay.
/// Input → Commands only (rule 2); sim state is read-only here.</summary>
public partial class Main : Node3D
{
    /// <summary>Interpolation alpha for UnitRenderer, refreshed every frame.</summary>
    public static double DrawAlpha;

    private SimWorld _sim = null!;
    private UnitStore _units = null!;
    private GameMap _map = null!;
    private readonly Dictionary<EntityId, MoveOrder> _orders = new();
    private readonly List<Command> _outbox = new();
    private double _accumulator;
    private double _simMs; // ms spent in the last step (smoothed)

    private UnitRenderer _renderer = null!;
    private OrdersInput _input = null!;
    private DebugOverlay _overlay = null!;
    private SelectionOverlay _selectionOverlay = null!;
    private RtsCamera _camera = null!;

    public override void _Ready()
    {
        // --- sim: small demo field so the build is playable standalone ---
        _sim = new SimWorld(seed: 1);
        _map = new GameMap(48, 32);
        _map.BlockRect(20, 0, 21, 12);
        _map.BlockRect(20, 19, 21, 31);
        var catalog = UnitCatalog.LoadFromDirectory(ProjectSettings.GlobalizePath("res://../content/units"));
        _units = new UnitStore();
        var rifleman = catalog.Get("rifleman");
        var conscript = catalog.Get("conscript");
        for (var i = 0; i < 6; i++) // local squad
            _units.Spawn(_sim.Ids.Next(),
                new FixVec2(Fix64.FromInt(4 + i % 3), Fix64.FromInt(12 + i / 3)),
                rifleman.Speed, rifleman.Radius,
                new UnitProfile(0, rifleman.Hp, rifleman.Sight, rifleman.Damage, rifleman.Range, rifleman.CooldownTicks, rifleman.Armor, rifleman.DamageType));
        for (var i = 0; i < 6; i++) // enemy squad (attack-move them to fight)
            _units.Spawn(_sim.Ids.Next(),
                new FixVec2(Fix64.FromInt(38 + i % 3), Fix64.FromInt(12 + i / 3)),
                conscript.Speed, conscript.Radius,
                new UnitProfile(1, conscript.Hp, conscript.Sight, conscript.Damage, conscript.Range, conscript.CooldownTicks, conscript.Armor, conscript.DamageType));

        _sim.Systems.Add((w, due) => OrderSystem.ApplyCommands(_orders, due, _units.Find));
        _sim.Systems.Add((w, due) => NavigationSystem.Step(_map, _units, _orders));
        _sim.Systems.Add((w, due) => CombatSystem.Step(_units, _orders, w.Tick));
        _sim.Systems.Add((w, due) => _units.DespawnDead());

        // --- 3D presentation ---
        BuildTestField();

        var camera = new RtsCamera();
        AddChild(camera);
        camera.Configure(new Vector3(24, 0, 16), new Vector2(48, 32));
        camera.Current = true;
        _camera = camera;

        _renderer = new UnitRenderer
        {
            Units = _units,
            SelectedIds = () => _selection.Selected,
            TargetPos = id =>
            {
                var t = _units.Find(new EntityId(id));
                return t == null ? null : UnitRenderer.ToWorld(t.Position.X, t.Position.Y);
            },
        };
        AddChild(_renderer);

        var sel = new SelectionController();
        _input = new OrdersInput
        {
            SelectedIds = () => _selection.Selected,
            Selection = sel,
            Units = _units,
            Camera = camera,
            Emit = c => _outbox.Add(c),
            CurrentTick = () => _sim.Tick,
            EnemiesOf = id => _units.Find(new EntityId(id)) is { } u && u.Faction != 0,
        };
        var selectionLayer = new CanvasLayer { Name = "SelectionHud" };
        AddChild(selectionLayer);
        _selectionOverlay = new SelectionOverlay
        {
            Name = "DragSelection",
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        selectionLayer.AddChild(_selectionOverlay);
        _input.DragBoxChanged = (a, b) =>
        {
            if (a == b) _selectionOverlay.Box = null;
            else
            {
                var left = Mathf.Min(a.X, b.X);
                var top = Mathf.Min(a.Y, b.Y);
                _selectionOverlay.Box = new Rect2(left, top, Mathf.Abs(b.X - a.X), Mathf.Abs(b.Y - a.Y));
            }
        };
        AddChild(_input);

        _overlay = new DebugOverlay();
        AddChild(_overlay);

        GD.Print($"sim ready, {SimWorld.TicksPerSecond} ticks/s, {_units.Units.Count} units");

        if (OS.GetCmdlineUserArgs().Any(a => a == "--smoke")) RunSmoke();
        if (OS.GetCmdlineUserArgs().Any(a => a == "--shots")) StartShots();
    }

    // --- scripted screenshot capture (--shots): proves the live 3D presentation
    // renders real models, walk cycles, and combat, using the actual game loop.
    // Rendering goes through an offscreen SubViewport with its own camera so the
    // captures do not depend on the window manager delivering compositor frames.
    // Run under a display server (Xwayland is enough): godot --path game -- --shots
    private static readonly (int Tick, string Name, Vector3 Target, float Distance)[] ShotList =
    {
        (5, "01_idle", new Vector3(6, 0, 12), 22f),
        (150, "02_march", new Vector3(16, 0, 12), 30f),
        (430, "03_engage", new Vector3(31, 0, 12), 34f),
        (520, "04_firefight", new Vector3(35, 0, 12), 30f),
        (650, "05_after", new Vector3(36, 0, 12), 34f),
    };

    private SubViewport? _shotViewport;
    private Camera3D? _shotCamera;
    private string? _pendingGrab;
    private readonly HashSet<int> _aimedTicks = new();
    private int _shotFrame;

    private void StartShots()
    {
        var shotViewport = new SubViewport
        {
            Size = new Vector2I(1280, 720),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        _shotViewport = shotViewport;
        AddChild(_shotViewport);
        _shotCamera = new Camera3D { Fov = 45.0f, Near = 0.05f, Far = 300.0f };
        _shotViewport.AddChild(_shotCamera);
        _shotViewport.World3D = _camera.GetWorld3D(); // share the live game world
        _shotFrame = 0;
    }

    private void AimShotCamera(Vector3 target, float distance)
    {
        var pitch = Mathf.DegToRad(52.0f);
        var offset = new Vector3(0, Mathf.Sin(pitch), Mathf.Cos(pitch)) * distance;
        _shotCamera!.GlobalPosition = target + offset;
        _shotCamera.LookAt(target, Vector3.Up);
    }

    private void ShotStep()
    {
        _shotFrame++;
        // grab last frame's rendered image before it changes
        if (_pendingGrab != null)
        {
            var dir = OS.GetEnvironment("RTS_SHOTS_DIR");
            if (string.IsNullOrEmpty(dir)) dir = "/tmp/rts-shots";
            DirAccess.MakeDirRecursiveAbsolute(dir);
            var path = dir + $"/{_pendingGrab}.png";
            var err = _shotViewport!.GetTexture().GetImage().SavePng(path);
            GD.Print($"SHOT_SAVED {_pendingGrab} tick={_sim.Tick} {path} err={err}");
            _pendingGrab = null;
        }
        var first = ShotList[0];
        if (_shotFrame == 1) AimShotCamera(first.Target, first.Distance);
        foreach (var (tick, name, target, distance) in ShotList)
        {
            if (_sim.Tick < tick || _aimedTicks.Contains(tick)) continue;
            _aimedTicks.Add(tick);
            AimShotCamera(target, distance);
            _pendingGrab = name; // saved on the next frame, after rendering
            break;
        }
        if (_shotFrame == 40) // after the idle beat, march the squad at the enemy
            _outbox.Add(new AttackMoveCommand(_sim.Tick, 0,
                _units.Units.Where(x => x.Faction == 0).Select(x => x.Id).ToArray(),
                new FixVec2(Fix64.FromInt(38), Fix64.FromInt(12)), false));
        if (_aimedTicks.Count >= ShotList.Length && _pendingGrab == null)
            GetTree().Quit();
    }

    private void BuildTestField()
    {
        var groundMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.18f, 0.23f, 0.20f),
            Roughness = 0.96f,
        };
        var ground = new MeshInstance3D
        {
            Name = "TestGround",
            Mesh = new PlaneMesh { Size = new Vector2(48, 32) },
            MaterialOverride = groundMaterial,
            Position = new Vector3(24, -0.06f, 16),
        };
        AddChild(ground);

        var gridMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.31f, 0.38f, 0.32f, 0.36f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var grid = new ImmediateMesh();
        grid.SurfaceBegin(Mesh.PrimitiveType.Lines, gridMaterial);
        for (var x = 0; x <= 48; x++)
        {
            grid.SurfaceAddVertex(new Vector3(x, 0.005f, 0));
            grid.SurfaceAddVertex(new Vector3(x, 0.005f, 32));
        }
        for (var z = 0; z <= 32; z++)
        {
            grid.SurfaceAddVertex(new Vector3(0, 0.005f, z));
            grid.SurfaceAddVertex(new Vector3(48, 0.005f, z));
        }
        grid.SurfaceEnd();
        AddChild(new MeshInstance3D { Name = "CellGrid", Mesh = grid, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        var obstacleMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.31f, 0.29f, 0.23f),
            Roughness = 1.0f,
        };
        AddObstacle("NorthBarrier", new Vector3(20.5f, 0.8f, 6), new Vector3(1, 1.6f, 12), obstacleMaterial);
        AddObstacle("SouthBarrier", new Vector3(20.5f, 0.8f, 25), new Vector3(1, 1.6f, 12), obstacleMaterial);

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.045f, 0.06f, 0.07f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.64f, 0.7f, 0.78f),
            AmbientLightEnergy = 0.65f,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D
        {
            Name = "KeyLight",
            RotationDegrees = new Vector3(-52, -32, 0),
            LightColor = new Color(1.0f, 0.91f, 0.77f),
            LightEnergy = 1.25f,
            ShadowEnabled = true,
        });
    }

    private void AddObstacle(string nodeName, Vector3 position, Vector3 size, Material material)
    {
        AddChild(new MeshInstance3D
        {
            Name = nodeName,
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = material,
            Position = position,
        });
    }

    /// <summary>Headless self-check (godot --path game --headless -- --smoke): emits commands
    /// through the same outbox path OrdersInput uses and asserts the sim reacts.</summary>
    private void RunSmoke()
    {
        var mine = _units.Units.Where(u => u.Faction == 0).Select(u => u.Id).ToArray();
        _outbox.Add(new AttackMoveCommand(_sim.Tick, 0, mine,
            new FixVec2(Fix64.FromInt(40), Fix64.FromInt(16)), false));
        var start = _units.Units[0].Position;
        for (var i = 0; i < 900 && _units.Units.Count(u => u.Faction == 1) > 0; i++)
        {
            _sim.Step(_outbox);
            _outbox.Clear();
        }
        var moved = (_units.Units[0].Position - start).Length;
        var enemiesLeft = _units.Units.Count(u => u.Faction == 1);
        if (moved.ToDouble() > 1.0 && enemiesLeft == 0)
            GD.Print($"SMOKE PASS unit0 moved {moved.ToDouble():F2} cells, enemy squad wiped");
        else
            GD.PrintErr($"SMOKE FAIL moved {moved.ToDouble():F2}, {enemiesLeft} enemies left");
        GetTree().Quit();
    }

    private readonly SelectionController _selection = new();

    public override void _Process(double delta)
    {
        if (_shotFrame >= 0 && OS.GetCmdlineUserArgs().Any(a => a == "--shots")) ShotStep();
        var sw = Stopwatch.StartNew();
        const double step = 1.0 / SimWorld.TicksPerSecond;
        _accumulator += delta;
        while (_accumulator >= step)
        {
            _sim.Step(_outbox);
            _outbox.Clear();
            _renderer.CaptureSnapshot(_sim.Tick);
            _accumulator -= step;
        }
        _selection.Prune(id => _units.Find(new EntityId(id)) != null);
        sw.Stop();
        _simMs = _simMs * 0.9 + sw.Elapsed.TotalMilliseconds * 0.1;
        DrawAlpha = _accumulator / step;

        if (_overlay.Enabled)
        {
            var sel = _selection.Selected.OrderBy(i => i).ToList();
            var sb = new System.Text.StringBuilder();
            sb.Append($"tick {_sim.Tick}  sim {_simMs:F2} ms/step  sel {sel.Count} [{string.Join(",", sel.Take(8))}]");
            foreach (var id in sel.Take(4))
            {
                var u = _units.Find(new EntityId(id));
                if (u == null) continue;
                // counter matrix visible to the player (counter-matrix.md: hidden matrices are a complaint engine)
                if (u.Damage.Raw > 0)
                {
                    var t = u.TargetId != EntityId.None ? _units.Find(u.TargetId) : null;
                    if (t != null)
                    {
                        var mult = Rts.Sim.Combat.DamageMatrix.Get(u.DamageType, t.Armor);
                        sb.Append($"\nu{id} {u.Damage.ToIntFloor()} {u.DamageType} -> u{t.Id.Value} {t.Armor} x{mult.ToDouble():F2} = {u.Damage.ToIntFloor() * mult.ToDouble():F1}");
                    }
                    else
                        sb.Append($"\nu{id} {u.Damage.ToIntFloor()} {u.DamageType} armor {u.Armor}");
                }
                if (_orders.TryGetValue(new EntityId(id), out var o) && o.Waypoint < o.Path.Count)
                {
                    sb.Append($" path {o.Path.Count - o.Waypoint}: ");
                    sb.Append(string.Join(" ", o.Path.Skip(o.Waypoint).Take(4)
                        .Select(p => $"({p.X.ToDouble():F1},{p.Y.ToDouble():F1})")));
                }
            }
            _overlay.SetText(sb.ToString());
        }
    }
}
