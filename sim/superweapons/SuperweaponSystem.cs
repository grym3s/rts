using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Sim.Superweapons;

/// <summary>Tick step 7, last: superweapons charge, fire on command, and strike after a telegraphed
/// delay (see CONTEXT.md). Deterministic: store order, no RNG, no floats.</summary>
public static class SuperweaponSystem
{
    public static bool Ready(Unit b, int tick) =>
        b.Ability.IsSuperweapon && b.Hp.Raw > 0 && b.Building is { ConstructionRemaining: 0 }
        && b.ChargeLeft == 0 && b.StrikeTick == 0 && !b.IsSabotaged(tick);

    public static void Step(UnitStore units, IReadOnlyList<Command> due, int tick, Func<int, bool>? lowPower = null, SimWorld? events = null)
    {
        foreach (var c in due)
        {
            if (c is not FireCommand f || units.Find(f.Building) is not { } b || b.Faction != f.Faction || !Ready(b, tick)) continue;
            var delay = Math.Max(1, b.Ability.StrikeDelayTicks);
            b.StrikeAt = f.Target;
            b.StrikeTick = tick + delay;
            b.PulsesLeft = Math.Max(1, b.Ability.StrikePulses);
            events?.Emit(SimEventKind.SuperweaponFired, b.Id, EntityId.None, Fix64.FromInt(delay), b.Faction);
        }

        foreach (var b in units.Units.ToList())
        {
            if (!b.Ability.IsSuperweapon || b.Hp.Raw <= 0 || b.Building is not { ConstructionRemaining: 0 }) continue;
            if (b.StrikeTick != 0)
            {
                if (tick < b.StrikeTick) continue;
                Strike(b, units, tick, events);
                if (--b.PulsesLeft > 0) { b.StrikeTick = tick + Math.Max(1, b.Ability.PulseGapTicks); continue; }
                b.StrikeTick = 0;
                b.PulsesLeft = 0;
                b.ChargeLeft = b.Ability.ChargeTicks; // and it charges again
                continue;
            }
            // charging stops while its side is low on power or it is sabotaged (economy.md)
            if (b.ChargeLeft > 0 && lowPower?.Invoke(b.Faction) != true && !b.IsSabotaged(tick)) b.ChargeLeft--;
        }
    }

    /// <summary>One pulse: StrikeDamage as Explosive through the counter matrix to every unit and
    /// building (any side but neutral; not passengers or phased units) within StrikeRadius.</summary>
    private static void Strike(Unit b, UnitStore units, int tick, SimWorld? events)
    {
        var r = b.Ability.StrikeRadius;
        foreach (var e in units.Units)
        {
            if (e.Hp.Raw <= 0 || e.Faction == Factions.Neutral || e.Inside != EntityId.None || e.IsPhased(tick)) continue;
            var reach = r + e.Radius + (e.Building != null ? Fix64.One : Fix64.Zero);
            if ((e.Position - b.StrikeAt).LengthSquared > reach * reach) continue;
            var applied = b.Ability.StrikeDamage * DamageMatrix.Get(DamageType.Explosive, e.Armor);
            applied = Support.WithShield(applied, e);
            if (applied.Raw < Fix64.OneRaw) applied = Fix64.One;
            e.Hp -= applied;
            events?.Emit(SimEventKind.Shot, b.Id, e.Id, applied);
            if (e.Hp.Raw <= 0) events?.Emit(SimEventKind.Death, e.Id, b.Id, faction: e.Faction);
        }
    }
}
