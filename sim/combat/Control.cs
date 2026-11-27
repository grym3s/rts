using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>Tri-Point rig hold-to-capture (map design: "engineer-or-hold"). Every building with
/// `Abilities.HoldCapture` (the oil derrick) counts each side's ground units within `HoldRadius`
/// (not buildings, aircraft or passengers). A side with a strict majority (more than all the
/// others together) that doesn't own it takes it after `HoldTicks` of holding; a tie or no
/// majority freezes the count; nobody there lets it fall back. States, for the presentation:
/// Neutral (nobody owns it or stands there), Occupied (a side is taking it), Contested (several
/// sides, no majority), Held (owned, empty), Fortified (owned and its owner holds the majority).
/// Runs after captures in step 7. Deterministic: store order, integer counts.</summary>
public static class Control
{
    public enum State { Neutral, Occupied, Contested, Held, Fortified }

    /// <summary>15 s of holding takes the rig.</summary>
    public const int HoldTicks = 15 * SimWorld.TicksPerSecond;

    public static void Step(UnitStore units, Dictionary<EntityId, MoveOrder> orders, int tick, SimWorld? events = null)
    {
        foreach (var r in units.Units)
        {
            if (!r.Ability.HoldCapture || r.Hp.Raw <= 0 || r.Building is { Wrecked: true }) continue;
            var (leader, majority, anyone) = Count(r, units);
            if (majority && leader != r.Faction)
            {
                if (r.HoldFaction != leader || r.HoldProgress == 0) { r.HoldFaction = leader; r.HoldProgress = 0; }
                if (++r.HoldProgress < HoldTicks) continue;
                r.HoldProgress = 0;
                CaptureSystem.Take(r, leader, units, orders, null);
                events?.Emit(SimEventKind.Captured, r.Id, EntityId.None, faction: leader);
                continue;
            }
            if (anyone && !majority) continue;           // contested: frozen
            if (r.HoldProgress > 0) r.HoldProgress--;    // owner holds it, or nobody: the count falls back
        }
    }

    /// <summary>The side with the most units near the rig, whether that is a strict majority, and
    /// whether anyone is there at all.</summary>
    public static (int Leader, bool Majority, bool Anyone) Count(Unit r, UnitStore units)
    {
        Span<int> n = stackalloc int[8];
        var r2 = r.Ability.HoldRadius * r.Ability.HoldRadius;
        var total = 0;
        foreach (var u in units.Units)
        {
            if (u.Building != null || u.Hp.Raw <= 0 || u.Inside != EntityId.None || u.Domain == MoveDomain.Air) continue;
            if ((uint)u.Faction >= 8 || (u.Position - r.Position).LengthSquared > r2) continue;
            n[u.Faction]++;
            total++;
        }
        if (total == 0) return (-1, false, false);
        var leader = 0;
        for (var f = 1; f < 8; f++) if (n[f] > n[leader]) leader = f;
        return (leader, n[leader] * 2 > total, true);
    }

    public static State StateOf(Unit r, UnitStore units)
    {
        var (leader, majority, anyone) = Count(r, units);
        var owned = r.Faction != Factions.Neutral;
        if (!anyone) return owned ? State.Held : State.Neutral;
        if (!majority) return State.Contested;
        return leader == r.Faction ? State.Fortified : State.Occupied;
    }
}
