using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Sim.Match;

/// <summary>Match outcome (T06). Derived from the unit store, so it never enters the state hash.</summary>
public sealed class MatchState
{
    /// <summary>True once one faction (or none) is left standing.</summary>
    public bool Over { get; internal set; }
    /// <summary>Winning faction, or -1 for a draw (the last units of every faction died the same tick).</summary>
    public int Winner { get; internal set; } = -1;
    /// <summary>Tick on which the match ended; -1 while it runs.</summary>
    public int EndTick { get; internal set; } = -1;
    /// <summary>Factions that have had a living unit at some point this match.</summary>
    public IReadOnlyCollection<int> Factions => _factions;

    internal readonly SortedSet<int> _factions = new();
}

/// <summary>Tick step 9 (after cleanup): a match between two or more factions ends when at most one
/// of them still has a living unit (buildings and harvesters count). Win = every enemy is dead;
/// lose = all your units are. A world that has only ever had one faction (economy or production
/// sandboxes) never ends. Emits one MatchEnded event, then does nothing more; the world keeps
/// stepping so replays and hashes are unaffected.</summary>
public static class MatchSystem
{
    public static void Step(SimWorld world, UnitStore units, MatchState match)
    {
        if (match.Over) return;

        var alive = new SortedSet<int>();
        foreach (var u in units.Units)
            if (u.Hp.Raw > 0) alive.Add(u.Faction);
        foreach (var f in alive) match._factions.Add(f);
        // a side wiped out this tick is already despawned: its Death events still count it in
        foreach (var e in world.Events)
            if (e.Kind == SimEventKind.Death) match._factions.Add(e.Faction);

        if (match._factions.Count < 2 || alive.Count > 1) return;

        match.Over = true;
        match.Winner = alive.Count == 1 ? alive.Min : -1;
        match.EndTick = world.Tick;
        world.Emit(SimEventKind.MatchEnded, EntityId.None, EntityId.None, faction: match.Winner);
    }
}
