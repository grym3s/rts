using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>Tick step 4 (see ../../CONTEXT.md): acquire targets, fire on cooldown, apply damage.
/// Cleanup (step 6) despawns the dead. Deterministic: store order everywhere, no RNG, no floats.</summary>
public static class CombatSystem
{
    /// <summary>One tick of combat. Chase distance: a unit ordered to attack a target outside its
    /// weapons range gets a fresh MoveOrder toward that target each tick it is idle-pathed.
    /// `events` (optional) receives a Shot per hit and a Death per kill (T09). `lowPower(faction)`
    /// (economy.md low-power rule): while true, that side's defense buildings hold fire.</summary>
    public static void Step(UnitStore units, Dictionary<EntityId, MoveOrder> orders, int tick, SimWorld? events = null,
        Func<int, bool>? lowPower = null)
    {
        OwnedVerbs.RefreshAuras(units, tick); // T29 Marking: post-move positions, before anyone fires
        Support.Refresh(units, tick, lowPower); // shields (Anchor, Warden) and cloaks, before anyone fires
        foreach (var u in units.Units)
        {
            if (u.Hp.Raw <= 0) continue; // killed earlier this tick: no actions
            if (u.Building is { ConstructionRemaining: > 0 }) continue; // a turret fires once it is built
            if (u.Building != null && u.Damage.Raw != 0 && (lowPower?.Invoke(u.Faction) == true || u.IsSabotaged(tick)))
            {
                u.TargetId = EntityId.None; // offline: no power (or sabotaged)
                continue;
            }
            if (u.Inside != EntityId.None) continue; // a passenger: garrisons fire for it (sim/transport)

            // auto-acquire for attack-move: nearest living enemy in sight, first wins on ties (store order)
            if (u.AttackMoving && !HasLiveTarget(u, units))
            {
                if (orders.ContainsKey(u.Id)) u.TargetId = Acquire(u, units, tick);
                else u.AttackMoving = false; // route finished: from here on the unit guards where it stopped
            }

            if (u.Damage.Raw != 0) Guard(u, units, orders, tick);

            if (u.CooldownRemaining > 0) u.CooldownRemaining--;

            if (u.TargetId == EntityId.None || u.Damage.Raw == 0) continue;
            var target = units.Find(u.TargetId);
            // T25: a weapon that can't engage the target's domain (rifle vs jet, torpedo vs tank)
            // drops it like a dead one: the order was impossible, not pending.
            if (target == null || target.Hp.Raw <= 0 || !CanHit(u, target) || !Support.CanSee(u.Faction, target, tick))
            {
                u.TargetId = EntityId.None;
                if (u.AttackMoving) continue; // resume route
                orders.Remove(u.Id);
                continue;
            }

            var dist = (target.Position - u.Position).Length;
            var fireRange = u.Range + u.Radius + target.Radius;
            if (dist <= fireRange)
            {
                // T29 Phase: a phased unit can't fire, and nothing can hit a phased target (the
                // attack waits; it is not dropped)
                if (u.CooldownRemaining == 0 && !u.IsPhased(tick) && !target.IsPhased(tick))
                {
                    // counter matrix: base damage x canon cell (tools/refmodel_counter_matrix.py).
                    // Chip floor: a live weapon always does >= 1 whole damage — soft-with-cliffs
                    // means chip, never bounce (ADR 0005); floor decided in the reference model.
                    Hit(u, target, u.Damage, tick, events);
                    if (u.Ability.LineDamage) LineHits(u, target, fireRange, units, tick, events);
                    u.CooldownRemaining = u.CooldownTicks;
                    if (u.Cloaked) u.RevealedUntilTick = tick + Support.RevealTicks; // firing gives a cloaked unit away
                    // Phase Turret: blinks out between shots (half its cooldown)
                    if (u.Ability.PhasesBetweenShots) u.PhaseUntilTick = tick + 1 + u.CooldownTicks / 2;
                }
            }
            else if (u.Speed.Raw == 0)
            {
                // static guard or a defense building: out of reach, look again next tick
                if (u.Guarding || u.Building != null) { u.TargetId = EntityId.None; u.Guarding = false; }
            }
            else if (!u.AttackMoving)
            {
                // chase: walk to the target unless already pathing there
                var o = orders.TryGetValue(u.Id, out var existing) ? existing : null;
                if (o == null || o.Target != target.Position)
                    orders[u.Id] = new MoveOrder(target.Position, new List<FixVec2>(), tick);
            }
        }
    }

    /// <summary>One hit of `damage` from `u`: the counter matrix, Marking ×1.25, shields, the chip
    /// floor (a live weapon always does >= 1, ADR 0005), on-hit marks, Shot / Death events.</summary>
    public static void Hit(Unit u, Unit target, Fix64 damage, int tick, SimWorld? events)
    {
        var applied = damage * DamageMatrix.Get(u.DamageType, target.Armor);
        applied = OwnedVerbs.WithMark(applied, u, target, tick); // T29 Marking ×1.25
        applied = Support.WithShield(applied, target);           // Anchor / Warden
        if (applied.Raw < Fix64.OneRaw) applied = Fix64.One;
        if (u.MarksOnHit) OwnedVerbs.Mark(target, u.Faction, tick + OwnedVerbs.OnHitTicks);
        var wasAlive = target.Hp.Raw > 0;
        target.Hp -= applied;
        events?.Emit(SimEventKind.Shot, u.Id, target.Id, applied);
        if (wasAlive && target.Hp.Raw <= 0)
            events?.Emit(SimEventKind.Death, target.Id, u.Id, faction: target.Faction);
    }

    /// <summary>Line damage: half-width of the line (plus the victim's radius).</summary>
    public static readonly Fix64 LineHalfWidth = Fix64.Ratio(1, 2);

    /// <summary>Rift weapons pierce: every other enemy the shot's line crosses, from the shooter
    /// out to full range through the target, takes the same hit.</summary>
    private static void LineHits(Unit u, Unit primary, Fix64 reach, UnitStore units, int tick, SimWorld? events)
    {
        var d = primary.Position - u.Position;
        var len = d.Length;
        if (len.Raw == 0) return;
        var dir = new FixVec2(d.X / len, d.Y / len);
        foreach (var e in units.Units)
        {
            if (e == primary || e.Faction == u.Faction || e.Hp.Raw <= 0 || e.Inside != EntityId.None) continue;
            if (!CanHit(u, e) || e.IsPhased(tick) || !Support.CanSee(u.Faction, e, tick)) continue;
            var rel = e.Position - u.Position;
            var along = rel.X * dir.X + rel.Y * dir.Y;
            if (along.Raw < 0 || along > reach) continue;
            var side = rel.X * dir.Y - rel.Y * dir.X;
            if (Fix64.Abs(side) > LineHalfWidth + e.Radius) continue;
            Hit(u, e, u.Damage, tick, events);
        }
    }

    /// <summary>Leash: a guard gives up a chase once it is this many sight ranges from its anchor.</summary>
    private static readonly Fix64 LeashSights = Fix64.Ratio(3, 2);

    /// <summary>T05 hold / chase / return. A unit with no order and no target holds its spot and
    /// engages the nearest enemy in sight (becoming Guarding, anchored where it stood). It chases
    /// until the leash or until nothing is left to fight in sight, then walks back (Returning,
    /// no acquiring on the way) and holds again. Any player order clears all of this.</summary>
    private static void Guard(Unit u, UnitStore units, Dictionary<EntityId, MoveOrder> orders, int tick)
    {
        var hasOrder = orders.ContainsKey(u.Id);
        if (u.Returning)
        {
            if (hasOrder) return;
            u.Returning = false; // home: hold again
        }
        if (u.AttackMoving) return;
        if (u.BridgeTarget >= 0) return; // T26: a bridge job is a player order: no auto-acquiring
        if (u.Building != null)
        {
            // a defense never moves: it shoots the nearest enemy in range (sight = range) and
            // looks again once that one dies or leaves
            if (!HasLiveTarget(u, units)) u.TargetId = Acquire(u, units, tick);
            return;
        }

        var hasLive = HasLiveTarget(u, units);
        if (u.Guarding)
        {
            var leash = u.Sight * LeashSights;
            if ((u.Position - u.GuardAnchor).LengthSquared > leash * leash)
            {
                ReturnToAnchor(u, orders, tick);
                return;
            }
            if (hasLive) return;
            var next = Acquire(u, units, tick);
            if (next != EntityId.None) { u.TargetId = next; return; }
            ReturnToAnchor(u, orders, tick);
            return;
        }

        if (hasOrder || hasLive) return; // under a player order or an explicit attack
        var found = Acquire(u, units, tick);
        if (found == EntityId.None) return;
        u.GuardAnchor = u.Position;
        u.Guarding = true;
        u.TargetId = found;
    }

    private static void ReturnToAnchor(Unit u, Dictionary<EntityId, MoveOrder> orders, int tick)
    {
        u.TargetId = EntityId.None;
        u.Guarding = false;
        u.Returning = true;
        orders[u.Id] = new MoveOrder(u.GuardAnchor, new List<FixVec2>(), tick);
    }

    private static bool HasLiveTarget(Unit u, UnitStore units)
    {
        if (u.TargetId == EntityId.None) return false;
        var t = units.Find(u.TargetId);
        return t != null && t.Hp.Raw > 0;
    }

    /// <summary>T25: the weapon's `targets` include the target's domain (buildings are ground).</summary>
    public static bool CanHit(Unit u, Unit target) =>
        target.Faction != Factions.Neutral && Domains.CanEngage(u.Targets, target.Domain); // T28: neutrals are untouchable

    /// <summary>Nearest enemy strictly inside sight range that the weapon can engage; store order
    /// breaks distance ties.</summary>
    private static EntityId Acquire(Unit u, UnitStore units, int tick)
    {
        var best = EntityId.None;
        var bestD = Fix64.Zero;
        foreach (var e in units.Units)
        {
            if (e.Faction == u.Faction || e.Hp.Raw <= 0 || !CanHit(u, e) || e.IsPhased(tick)) continue; // T29: phased = untargetable
            if (e.Inside != EntityId.None || !Support.CanSee(u.Faction, e, tick)) continue; // carried, or cloaked and undetected
            var d = (e.Position - u.Position).LengthSquared;
            if (d > u.Sight * u.Sight) continue;
            if (best == EntityId.None || d < bestD)
            {
                best = e.Id;
                bestD = d;
            }
        }
        return best;
    }
}
