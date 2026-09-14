using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Rts.Sim.Core;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>Root scene: owns the SimWorld, steps it at a fixed rate, drives render/overlay.
/// Input → Commands only (rule 2); sim state is read-only here.</summary>
public partial class Main : Node2D
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
    private (Vector2 A, Vector2 B)? _dragBox;

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
        for (var i = 0; i < 12; i++)
            _units.Spawn(_sim.Ids.Next(),
                new FixVec2(Fix64.FromInt(4 + i % 4), Fix64.FromInt(10 + i / 4)),
                rifleman.Speed, rifleman.Radius);

        _sim.Systems.Add((w, due) => OrderSystem.ApplyCommands(_orders, due));
        _sim.Systems.Add((w, due) => NavigationSystem.Step(_map, _units, _orders));

        // --- presentation ---
        var camera = new RtsCamera { Position = new Vector2(24 * UnitRenderer.CellSize, 16 * UnitRenderer.CellSize) };
        AddChild(camera);
        camera.MakeCurrent();

        _renderer = new UnitRenderer
        {
            Units = _units,
            SelectedIds = () => _selection.Selected,
            DragBox = () => _dragBox,
        };
        AddChild(_renderer);

        var sel = new SelectionController();
        _input = new OrdersInput
        {
            SelectedIds = () => _selection.Selected,
            Selection = sel,
            Units = _units,
            Emit = c => _outbox.Add(c),
            CurrentTick = () => _sim.Tick,
        };
        _input.DragBoxChanged = (a, b) => _dragBox = a == b ? null : (a, b);
        AddChild(_input);

        _overlay = new DebugOverlay();
        AddChild(_overlay);

        GD.Print($"sim ready, {SimWorld.TicksPerSecond} ticks/s, {_units.Units.Count} units");

        if (OS.GetCmdlineUserArgs().Any(a => a == "--smoke")) RunSmoke();
    }

    /// <summary>Headless self-check (godot --path game --headless -- --smoke): emits commands
    /// through the same outbox path OrdersInput uses and asserts the sim reacts.</summary>
    private void RunSmoke()
    {
        var ids = _units.Units.Select(u => u.Id).ToArray();
        _outbox.Add(new MoveCommand(_sim.Tick, 0, ids,
            new FixVec2(Fix64.FromInt(40), Fix64.FromInt(16)), false));
        var start = _units.Units[0].Position;
        for (var i = 0; i < 300; i++)
        {
            _sim.Step(_outbox);
            _outbox.Clear();
        }
        var moved = (_units.Units[0].Position - start).Length;
        if (moved.ToDouble() > 1.0 && ids.Length > 0)
            GD.Print($"SMOKE PASS unit0 moved {moved.ToDouble():F2} cells");
        else
            GD.PrintErr($"SMOKE FAIL unit0 moved {moved.ToDouble():F2}");
        GetTree().Quit();
    }

    private readonly SelectionController _selection = new();

    public override void _Process(double delta)
    {
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
        sw.Stop();
        _simMs = _simMs * 0.9 + sw.Elapsed.TotalMilliseconds * 0.1;
        DrawAlpha = _accumulator / step;

        if (_overlay.Enabled)
        {
            var sel = _selection.Selected.OrderBy(i => i).ToList();
            var sb = new System.Text.StringBuilder();
            sb.Append($"tick {_sim.Tick}  sim {_simMs:F2} ms/step  sel {sel.Count} [{string.Join(",", sel.Take(8))}]");
            foreach (var id in sel.Take(4))
                if (_orders.TryGetValue(new EntityId(id), out var o) && o.Waypoint < o.Path.Count)
                {
                    sb.Append($"\nu{id} path {o.Path.Count - o.Waypoint}: ");
                    sb.Append(string.Join(" ", o.Path.Skip(o.Waypoint).Take(4)
                        .Select(p => $"({p.X.ToDouble():F1},{p.Y.ToDouble():F1})")));
                }
            _overlay.SetText(sb.ToString());
        }
    }
}
