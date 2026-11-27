using Rts.Sim.Core;

namespace Rts.Sim.World;

/// <summary>What a unit or building can do beyond moving and shooting, read once from its
/// content file's `verbs` block (sim/units parses it; content/CONTEXT.md lists the keys).
/// Immutable and copied at spawn, so never hashed: the mutable state each ability needs lives
/// on `Unit`. `None` = nothing special. The systems that act on each group are named beside it.</summary>
public sealed record Abilities
{
    public static readonly Abilities None = new();

    // ---- combat (sim/combat CombatSystem) ----
    /// <summary>Phase Turret: phased (untargetable) for half its cooldown after every shot.</summary>
    public bool PhasesBetweenShots { get; init; }
    /// <summary>Rift Node / Lance / Hull: each shot also hits every enemy on the line out to full range.</summary>
    public bool LineDamage { get; init; }

    // ---- economy (sim/economy) ----
    /// <summary>Cultivator (Wellspring): what it harvests is never taken from the field.</summary>
    public bool NeverDepletes { get; init; }

    // ---- support (sim/combat Support) ----
    /// <summary>Anchor / Warden: allies within ShieldRadius take ShieldPercent less damage (no stacking).
    /// ShieldNavalOnly: only ships (Warden shields the fleet).</summary>
    public Fix64 ShieldRadius { get; init; }
    public int ShieldPercent { get; init; }
    public bool ShieldNavalOnly { get; init; }
    /// <summary>Field Repair: heals other allied units (not buildings) within HealRadius.</summary>
    public Fix64 HealRadius { get; init; }
    public Fix64 HealPerTick { get; init; }

    // ---- infiltrators (sim/combat CaptureSystem) ----
    /// <summary>Operative: a capture job on an enemy building shuts it down for SabotageTicks (consumed).</summary>
    public bool Sabotages { get; init; }
    /// <summary>Hijacker: a capture job on an enemy ground vehicle steals it (consumed).</summary>
    public bool Hijacks { get; init; }

    // ---- carriers (sim/combat Carriers) ----
    /// <summary>Carrier: keeps up to Wing of unit `Launches` alive, rebuilding one every
    /// RebuildTicks; they fight around it within LaunchRange.</summary>
    public string? Launches { get; init; }
    public int Wing { get; init; }
    public int RebuildTicks { get; init; }
    public Fix64 LaunchRange { get; init; }

    // ---- transport (sim/transport) ----
    /// <summary>Transport / garrison / tunnel: how many passengers fit; Carries: "infantry" or "ground".</summary>
    public int Capacity { get; init; }
    public string Carries { get; init; } = "infantry";
    /// <summary>Bunkers: passengers shoot out from it (with GarrisonRangeBonus).</summary>
    public bool Garrison { get; init; }
    /// <summary>Tunnel Network: one shared hold per faction, enter at any tunnel, leave at any.</summary>
    public bool Tunnel { get; init; }
    /// <summary>Teleport Node: units near one can be sent to another instantly.</summary>
    public bool Teleport { get; init; }

    // ---- stealth and detection (sim/combat Stealth) ----
    /// <summary>Cloaked unless an enemy detector is near (or it fired in the last RevealTicks).</summary>
    public bool Stealth { get; init; }
    /// <summary>Mindbender: cloaks friendly units within CloakRadius (and itself).</summary>
    public Fix64 CloakRadius { get; init; }
    /// <summary>Spotlight Bunker / Scout Halftrack: reveals cloaked enemies within DetectRadius.</summary>
    public Fix64 DetectRadius { get; init; }
    /// <summary>Seam Radar: its owner sees this far around it (vision only: fog is presentation).</summary>
    public Fix64 RadarRadius { get; init; }

    // ---- hold-to-capture (sim/combat Control) ----
    /// <summary>Tri-Point rig: a side with the majority of units within HoldRadius takes it over time.</summary>
    public bool HoldCapture { get; init; }
    public Fix64 HoldRadius { get; init; }

    // ---- superweapons (sim/superweapons) ----
    /// <summary>Charges for ChargeTicks (paused at low power), then a `fire` strikes a point:
    /// StrikeDamage within StrikeRadius, StrikePulses times, StrikeDelayTicks after the order.</summary>
    public int ChargeTicks { get; init; }
    public Fix64 StrikeRadius { get; init; }
    public Fix64 StrikeDamage { get; init; }
    public int StrikePulses { get; init; } = 1;
    public int StrikeDelayTicks { get; init; }
    public int PulseGapTicks { get; init; } = 10;

    public bool IsCarrier => Launches != null && Wing > 0;
    public bool IsSuperweapon => ChargeTicks > 0;
    public bool Holds => Capacity > 0 || Tunnel;
}
