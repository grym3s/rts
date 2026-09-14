using Rts.Sim.Core;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.Production;

/// <summary>Canon rules for what may be built/queued where (docs/factions/CONTEXT.md
/// "Timing ladders" + building tables; content ids are the prereq source of truth).
/// Pure checks — no money, no spawning; the system wires EconomyStore and UnitStore.</summary>
public static class ProductionRules
{
    /// <summary>Prereq chain: every `prereq` link must be satisfied by an alive building.
    /// `aliveOf(prereqBuildingCatalogId)` is the composition layer's faction-scoped lookup.</summary>
    public static bool PrereqSatisfied(BuildingCatalog buildingCatalog, string buildingId, Func<string, bool> aliveOf)
    {
        var guard = 0;
        var next = buildingCatalog.Get(buildingId).Prereq;
        while (next != null)
        {
            if (!aliveOf(next)) return false;
            next = buildingCatalog.Get(next).Prereq;
            if (++guard > 16) throw new InvalidOperationException($"prereq cycle at {buildingId}");
        }
        return true;
    }

    /// <summary>The cell a completed unit spawns on: deterministic outward ring scan
    /// (spiral offsets, lowest radius then angle) for the first walkable, unoccupied cell.</summary>
    public static FixVec2 FindSpawnCell(Rts.Sim.World.GameMap map, UnitStore units, FixVec2 buildingCell)
    {
        var bx = (int)(buildingCell.X.Raw >> Fix64.FractionBits);
        var by = (int)(buildingCell.Y.Raw >> Fix64.FractionBits);
        for (var r = 1; r <= 4; r++)
            for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue; // ring only
                    var (cx, cy) = (bx + dx, by + dy);
                    if (!map.IsWalkable(cx, cy)) continue;
                    var cell = new FixVec2(Fix64.FromInt(cx), Fix64.FromInt(cy));
                    var occupied = false;
                    foreach (var u in units.Units)
                    {
                        var ux = (int)(u.Position.X.Raw >> Fix64.FractionBits);
                        var uy = (int)(u.Position.Y.Raw >> Fix64.FractionBits);
                        if (ux == cx && uy == cy) { occupied = true; break; }
                    }
                    if (!occupied) return cell;
                }
        return buildingCell; // full ring: spawn on top (separation resolves)
    }
}
