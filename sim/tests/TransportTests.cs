using Rts.Sim.Core;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

/// <summary>sim/transport: transports, bunkers, the Tunnel Network and Teleport Nodes, on the
/// real match wiring (Arena).</summary>
public class TransportTests
{
    private static LoadCommand Load(Arena a, Unit holder, params Unit[] who) =>
        new(a.W.Tick, holder.Faction, who.Select(u => u.Id).ToArray(), holder.Id);

    [Fact]
    public void Infantry_board_an_apc_ride_with_it_and_get_out_where_it_stops()
    {
        var a = new Arena();
        var apc = a.Unit("troop-carrier", 1, 20.5, 20.5);
        var men = Enumerable.Range(0, 3).Select(i => a.Unit("conscript", 1, 14.5, 18.5 + 2 * i)).ToArray();
        a.Step(Load(a, apc, men));
        a.RunUntil(() => men.All(m => m.Inside == apc.Id), 300);
        Assert.All(men, m => Assert.Equal(apc.Id, m.Inside));

        a.Step(new MoveCommand(a.W.Tick, 1, new[] { apc.Id }, GameMap.CellCenter(36, 36), false));
        a.RunUntil(() => (apc.Position - GameMap.CellCenter(36, 36)).Length < Fix64.One, 600);
        Assert.All(men, m => Assert.True((apc.Position - m.Position).Length < Fix64.Half)); // riding along (a tick behind)
        a.Step(new UnloadCommand(a.W.Tick, 1, apc.Id));
        Assert.All(men, m => Assert.Equal(EntityId.None, m.Inside));
        Assert.All(men, m => Assert.True((m.Position - apc.Position).Length < Fix64.FromInt(3)));
    }

    [Fact]
    public void An_apc_takes_infantry_only_and_no_more_than_it_holds()
    {
        var a = new Arena();
        var ifv = a.Unit("ifv", 0, 20.5, 20.5); // 6 slots
        var tank = a.Unit("lancer-mbt", 0, 17.5, 20.5);
        var men = Enumerable.Range(0, 8).Select(i => a.Unit("rifleman", 0, 16.5 + (i % 4), 23.5 + i / 4)).ToArray();
        a.Step(Load(a, ifv, men.Append(tank).ToArray()));
        Assert.Equal(EntityId.None, tank.BoardTarget); // not infantry
        a.Run(300);
        Assert.Equal(6, men.Count(m => m.Inside == ifv.Id));
    }

    [Fact]
    public void Everyone_inside_dies_with_a_transport()
    {
        var a = new Arena();
        var apc = a.Unit("troop-carrier", 1, 20.5, 20.5);
        var man = a.Unit("conscript", 1, 21.5, 20.5);
        a.Step(Load(a, apc, man));
        a.RunUntil(() => man.Inside == apc.Id, 100);
        apc.Hp = Fix64.Zero;
        a.Step();
        Assert.True(man.Hp.Raw <= 0);
    }

    [Fact]
    public void A_landing_ship_picks_up_from_the_shore_and_lands_them_on_another()
    {
        // land | 10 water columns | land
        var rows = Enumerable.Repeat(new string('.', 10) + new string('~', 10) + new string('.', 10), 20);
        var a = new Arena(map: """{"width":30,"height":20,"terrain":[""" + string.Join(",", rows.Select(r => "\"" + r + "\"")) + "]}");
        var ship = a.Unit("landing-ship", 0, 11.5, 10.5);
        var tank = a.Unit("lancer-mbt", 0, 6.5, 10.5);
        var man = a.Unit("rifleman", 0, 6.5, 12.5);
        a.Step(Load(a, ship, tank, man));
        a.RunUntil(() => tank.Inside == ship.Id && man.Inside == ship.Id, 400);
        Assert.Equal(ship.Id, tank.Inside);
        a.Step(new MoveCommand(a.W.Tick, 0, new[] { ship.Id }, GameMap.CellCenter(18, 10), false));
        a.RunUntil(() => GameMap.CellOf(ship.Position.X) == 18, 400);
        a.Step(new UnloadCommand(a.W.Tick, 0, ship.Id));
        Assert.Equal(EntityId.None, tank.Inside);
        Assert.True(GameMap.CellOf(tank.Position.X) >= 20); // on the far shore
        Assert.True(a.M.Map.IsWalkable(GameMap.CellOf(man.Position.X), GameMap.CellOf(man.Position.Y), MoveDomain.Land));
    }

    [Fact]
    public void Infantry_in_a_bunker_shoot_out_of_it_and_cannot_be_shot()
    {
        var a = new Arena();
        var bunker = a.Building("hegemony-bunker", 1, 20, 20);
        a.Building("hegemony-furnace", 1, 6, 6);
        var man = a.Unit("conscript", 1, 18.5, 20.5);
        a.Step(Load(a, bunker, man));
        a.RunUntil(() => man.Inside == bunker.Id, 100);
        var enemy = a.Unit("rifleman", 0, 26.5, 20.5);
        enemy.Hp = Fix64.FromInt(10000);
        a.Run(100);
        Assert.Contains(a.Seen, e => e.Kind == SimEventKind.Shot && e.Subject == man.Id);
        Assert.DoesNotContain(a.Seen, e => e.Kind == SimEventKind.Shot && e.Target == man.Id);
    }

    [Fact]
    public void Units_enter_one_tunnel_and_leave_by_another()
    {
        var a = new Arena();
        var t1 = a.Building("hegemony-tunnel-network", 1, 8, 8);
        var t2 = a.Building("hegemony-tunnel-network", 1, 40, 40);
        var tank = a.Unit("bulwark", 1, 11.5, 8.5);
        a.Step(Load(a, t1, tank));
        a.RunUntil(() => tank.Inside == t1.Id, 200);
        a.Step(new UnloadCommand(a.W.Tick, 1, t2.Id));
        Assert.Equal(EntityId.None, tank.Inside);
        Assert.True((tank.Position - t2.Position).Length < Fix64.FromInt(5));
        // and the network outlives a lost tunnel
        a.Step(Load(a, t2, tank));
        a.RunUntil(() => tank.Inside == t2.Id, 200);
        t2.Hp = Fix64.Zero;
        a.Step();
        Assert.Equal(t1.Id, tank.Inside);
        Assert.True(tank.Hp.Raw > 0);
    }

    [Fact]
    public void Units_near_one_teleport_node_appear_at_another()
    {
        var a = new Arena();
        a.Building("ascendant-teleport-node", 2, 8, 8);
        var far = a.Building("ascendant-teleport-node", 2, 40, 40);
        var adept = a.Unit("adept", 2, 10.5, 8.5);
        var stray = a.Unit("adept", 2, 25.5, 25.5); // near no node
        a.Step(new TeleportCommand(a.W.Tick, 2, new[] { adept.Id, stray.Id }, far.Id));
        Assert.True((adept.Position - far.Position).Length < Fix64.FromInt(4));
        Assert.Equal(GameMap.CellCenter(25, 25), stray.Position);
    }
}
