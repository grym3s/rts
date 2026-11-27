using Rts.Sim.Core;

namespace Rts.Sim.Ai;

public sealed class AiPlayers
{
    private readonly List<int> _factions = new();
    public IReadOnlyList<int> Factions => _factions;
    public void Add(int faction) { if (!_factions.Contains(faction)) _factions.Add(faction); }
    public IEnumerable<Command> Think(SimWorld w, Rts.Sim.Setup.MatchWorld m) => Array.Empty<Command>();
}
