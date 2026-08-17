using Godot;
using Rts.Sim.Core;

namespace Rts.Game;

/// <summary>Root scene: owns the SimWorld, steps it at a fixed rate, and will hand SimState to renderers. Input → Commands only.</summary>
public partial class Main : Node2D
{
    private SimWorld _sim = null!;
    private readonly List<Command> _outbox = new();
    private double _accumulator;

    public override void _Ready()
    {
        _sim = new SimWorld(seed: 1);
        GD.Print($"sim ready, {SimWorld.TicksPerSecond} ticks/s");
    }

    public override void _Process(double delta)
    {
        const double step = 1.0 / SimWorld.TicksPerSecond;
        _accumulator += delta;
        while (_accumulator >= step)
        {
            _sim.Step(_outbox);
            _outbox.Clear();
            _accumulator -= step;
        }
    }
}
