using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Transport;

public static class TransportSystem
{
    public static void Step(UnitStore units, Dictionary<EntityId, MoveOrder> orders, GameMap map,
        IReadOnlyList<Command> due, int tick, SimWorld? events = null)
    {
    }
}
