using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>T29: the three owned verbs' structural knobs and the shared rules
/// (design canon: docs/factions/owned-verbs.md). Marking resolves inside the combat step,
/// Phase is set by a command (sim/orders) and read by combat targeting, Conscription is a
/// command resolved with production (it refunds credits and can detonate a Sapper).</summary>
public static class OwnedVerbs
{
    // ---- Marking (Coalition) ----
    /// <summary>Marked targets take ×1.25 from the marking faction, after the matrix cell.</summary>
    public static readonly Fix64 MarkBonus = Fix64.Ratio(5, 4);
    /// <summary>Aura marks linger 1.5 s (30 t) after the source loses the target.</summary>
    public const int AuraLingerTicks = 30;
    /// <summary>On-hit marks last 2 s (40 t) from the last hit.</summary>
    public const int OnHitTicks = 40;

    // ---- Phase (Ascendant) ----
    public const int PhaseTicks = 40;         // 2 s untargetable, can't fire
    public const int PhaseCooldownTicks = 200; // 10 s, counted from when Phase ends

    // ---- Conscription (Hegemony) ----
    public const int HallRefundPercent = 50;
    public const int FieldRefundPercent = 30;
    public static readonly Fix64 HallRadius = Fix64.FromInt(10);
    /// <summary>Sapper detonation: Explosive burst, radius 3. Base damage is a balance-pass
    /// magnitude (canon leaves it open); 60 resolves through the matrix like any Explosive hit.</summary>
    public static readonly Fix64 SapperRadius = Fix64.FromInt(3);
    public static readonly Fix64 SapperDamage = Fix64.FromInt(60);

    /// <summary>Refund for conscripting a unit of this cost: floor(cost × pct / 100), never above cost.</summary>
    public static int Refund(int cost, bool atHall) => cost * (atHall ? HallRefundPercent : FieldRefundPercent) / 100;

    /// <summary>Aura marking, once per combat step before anyone fires. A faction's aura sources
    /// (units and finished buildings with MarkRadius) work only while it has a finished aura
    /// building (the Sensor Command network gate). Each marks every living, unphased enemy
    /// within its radius until tick + linger. Line of sight is not modelled yet: radius only.</summary>
    public static void RefreshAuras(UnitStore units, int tick)
    {
        Span<bool> gate = stackalloc bool[8];
        var any = false;
        foreach (var s in units.Units)
            if (s.MarkRadius.Raw > 0 && s.Building is { ConstructionRemaining: 0 } && s.Hp.Raw > 0 && (uint)s.Faction < 8)
                gate[s.Faction] = any = true;
        if (!any) return;
        foreach (var s in units.Units)
        {
            if (s.MarkRadius.Raw <= 0 || s.Hp.Raw <= 0 || (uint)s.Faction >= 8 || !gate[s.Faction]) continue;
            if (s.Building is { ConstructionRemaining: > 0 }) continue;
            var r2 = s.MarkRadius * s.MarkRadius;
            foreach (var e in units.Units)
            {
                if (e.Faction == s.Faction || e.Hp.Raw <= 0 || e.IsPhased(tick)) continue;
                if ((e.Position - s.Position).LengthSquared > r2) continue;
                Mark(e, s.Faction, tick + AuraLingerTicks);
            }
        }
    }

    public static void Mark(Unit target, int byFaction, int untilTick)
    {
        if (target.MarkedBy == byFaction && target.MarkedUntilTick >= untilTick) return;
        target.MarkedBy = byFaction;
        target.MarkedUntilTick = untilTick;
    }

    /// <summary>Damage after Marking: ×1.25 when the target is marked by the shooter's faction.</summary>
    public static Fix64 WithMark(Fix64 applied, Unit shooter, Unit target, int tick) =>
        target.MarkedBy == shooter.Faction && tick <= target.MarkedUntilTick ? applied * MarkBonus : applied;
}
