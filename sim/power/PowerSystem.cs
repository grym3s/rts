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
        // Reset net power for all factions. Hardcoded to factions 0 and 1 (2-faction
        // game). Extend if more factions are added.
        power.Set(0, 0);
        power.Set(1, 0);

        // Sum supply and draw over alive, construction-complete buildings.
        foreach (var u in units.Units)
        {
            if (u.Building is not { } b) continue;
            if (u.Hp.Raw <= 0) continue; // dead
            if (b.ConstructionRemaining > 0) continue; // under construction
            var bp = buildings.Get(b.Id);
            power.Set(u.Faction, power.Net(u.Faction) + bp.Power);
        }
    }
}
