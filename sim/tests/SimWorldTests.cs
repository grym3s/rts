using Rts.Sim.Core;
using Xunit;

namespace Rts.Sim.Tests;

public class SimWorldTests
{
    [Fact]
    public void Same_seed_and_commands_give_same_hash()
    {
        var a = new SimWorld(7);
        var b = new SimWorld(7);
        var cmds = new Command[] { new StopCommand(0, 0, Array.Empty<EntityId>()) };
        for (var i = 0; i < 50; i++) { a.Step(cmds); b.Step(cmds); }
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(50, a.Tick);
    }
}
