using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.Power;

/// <summary>Tick step 2: recompute net power per faction. Sum(supply) − sum(draw) over
/// alive, construction-complete buildings. Under-construction buildings are excluded.
/// The parity gate is applied in ProductionSystem.Step (per-building, per-tick).</summary>
public static class PowerSystem
{
    public static void Step(PowerStore power, BuildingCatalog buildings, UnitStore units)
    {
        // Factions 0 and 1 always get a ledger (2-faction game); others appear when they own
        // a building. Sum supply (positive power) and draw (negative) separately over alive,
        // construction-complete buildings; net = supply - draw, exactly as before.
        var supply = new SortedDictionary<int, int> { [0] = 0, [1] = 0 };
        var draw = new SortedDictionary<int, int> { [0] = 0, [1] = 0 };
        foreach (var u in units.Units)
        {
            if (u.Building is not { } b) continue;
            if (u.Hp.Raw <= 0) continue; // dead
            if (u.Faction == Rts.Sim.World.Factions.Neutral) continue; // T28: unowned structures
            if (b.ConstructionRemaining > 0) continue; // under construction
            var p = buildings.Get(b.Id).Power;
            supply.TryAdd(u.Faction, 0);
            draw.TryAdd(u.Faction, 0);
            if (p >= 0) supply[u.Faction] += p; else draw[u.Faction] -= p;
        }
        foreach (var f in supply.Keys) power.SetLedger(f, supply[f], draw[f]);
    }
}
