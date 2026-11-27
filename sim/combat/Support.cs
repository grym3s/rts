using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>Area effects recomputed at the start of every combat step from post-move positions
/// (like Marking auras): shields (Anchor, Warden), Field Repair healing, and stealth: who is
/// cloaked (stealth units, the Mindbender's area cloak) and which sides detect them (Spotlight
/// Bunker, Scout Halftrack). The flags they set on `Unit` are derived each tick from hashed state,
/// so they are not hashed themselves. Deterministic: store order, no RNG, no floats.</summary>
public static class Support
{
    /// <summary>A cloaked unit that fires can be seen (and shot) for 2 s.</summary>
    public const int RevealTicks = 40;

    public static void Refresh(UnitStore units, int tick, Func<int, bool>? lowPower = null)
    {
        bool shields = false, heals = false, cloaks = false, detects = false;
        foreach (var u in units.Units)
        {
            u.ShieldPercentNow = 0;
            u.Cloaked = false;
            u.DetectedMask = 0;
            var a = u.Ability;
            if (ReferenceEquals(a, Abilities.None)) continue;
            shields |= a.ShieldPercent > 0;
            heals |= a.HealRadius.Raw > 0;
            cloaks |= a.Stealth || a.CloakRadius.Raw > 0;
            detects |= a.DetectRadius.Raw > 0;
        }

        if (shields)
            foreach (var s in units.Units)
            {
                if (s.Ability.ShieldPercent <= 0 || !Active(s, lowPower, tick)) continue;
                var r2 = s.Ability.ShieldRadius * s.Ability.ShieldRadius;
                foreach (var e in units.Units)
                {
                    if (e.Faction != s.Faction || e.Hp.Raw <= 0 || e.Inside != EntityId.None) continue;
                    if (s.Ability.ShieldNavalOnly && e.Domain != MoveDomain.Naval) continue;
                    if ((e.Position - s.Position).LengthSquared > r2) continue;
                    if (s.Ability.ShieldPercent > e.ShieldPercentNow) e.ShieldPercentNow = s.Ability.ShieldPercent;
                }
            }

        if (heals)
            foreach (var h in units.Units)
            {
                if (h.Ability.HealRadius.Raw <= 0 || !Active(h, lowPower, tick)) continue;
                var r2 = h.Ability.HealRadius * h.Ability.HealRadius;
                foreach (var e in units.Units)
                {
                    if (e == h || e.Faction != h.Faction || e.Building != null || e.Hp.Raw <= 0 || e.Hp >= e.MaxHp) continue;
                    if (e.Inside != EntityId.None || (e.Position - h.Position).LengthSquared > r2) continue;
                    e.Hp = Fix64.Min(e.MaxHp, e.Hp + h.Ability.HealPerTick);
                }
            }

        if (!cloaks) return;
        foreach (var u in units.Units)
        {
            if (u.Hp.Raw <= 0) continue;
            if (u.Ability.Stealth) u.Cloaked = true;
            if (u.Ability.CloakRadius.Raw <= 0 || u.Inside != EntityId.None) continue;
            u.Cloaked = true; // the Mindbender hides itself too
            var r2 = u.Ability.CloakRadius * u.Ability.CloakRadius;
            foreach (var e in units.Units)
                if (e.Faction == u.Faction && e.Building == null && e.Hp.Raw > 0 && (e.Position - u.Position).LengthSquared <= r2)
                    e.Cloaked = true;
        }
        if (!detects) return;
        foreach (var d in units.Units)
        {
            if (d.Ability.DetectRadius.Raw <= 0 || !Active(d, lowPower, tick) || (uint)d.Faction >= 32) continue;
            var r2 = d.Ability.DetectRadius * d.Ability.DetectRadius;
            foreach (var e in units.Units)
                if (e.Cloaked && e.Faction != d.Faction && (e.Position - d.Position).LengthSquared <= r2)
                    e.DetectedMask |= 1u << d.Faction;
        }
    }

    /// <summary>A live source that is not carried; a building one must be finished, powered and not sabotaged.</summary>
    private static bool Active(Unit s, Func<int, bool>? lowPower, int tick) =>
        s.Hp.Raw > 0 && s.Inside == EntityId.None
        && (s.Building == null || (s.Building.ConstructionRemaining == 0 && lowPower?.Invoke(s.Faction) != true && !s.IsSabotaged(tick)));

    /// <summary>Whether `faction` may target `t`: not cloaked, or detected by that side, or it
    /// fired in the last RevealTicks.</summary>
    public static bool CanSee(int faction, Unit t, int tick) =>
        !t.Cloaked || ((uint)faction < 32 && (t.DetectedMask & (1u << faction)) != 0) || tick < t.RevealedUntilTick;

    /// <summary>Damage after a shield: ShieldPercent less.</summary>
    public static Fix64 WithShield(Fix64 damage, Unit t) =>
        t.ShieldPercentNow > 0 ? damage * Fix64.Ratio(100 - t.ShieldPercentNow, 100) : damage;
}
