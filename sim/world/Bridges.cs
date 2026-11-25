using Rts.Sim.Core;

namespace Rts.Sim.World;

/// <summary>T26: one bridge: a 4-connected group of `=` cells with its own hit points. Not a
/// unit (no faction, never in the roster, never auto-targeted, never counted by the match).</summary>
public sealed class Bridge
{
    public int Id { get; }
    /// <summary>Cells in row-major order (y, then x).</summary>
    public IReadOnlyList<(int X, int Y)> Cells { get; }
    public Fix64 MaxHp { get; }
    public Fix64 Hp { get; set; }
    /// <summary>Down: its cells are water (ground units can't cross, ships still pass).</summary>
    public bool Broken { get; set; }
    public bool Damaged => Broken || Hp < MaxHp;

    public Bridge(int id, IReadOnlyList<(int X, int Y)> cells, Fix64 hp)
    {
        Id = id;
        Cells = cells;
        MaxHp = hp;
        Hp = hp;
    }

    public bool Covers(int cx, int cy)
    {
        foreach (var c in Cells) if (c.X == cx && c.Y == cy) return true;
        return false;
    }

    /// <summary>Centre of the cell nearest to p (ties: first in row-major order).</summary>
    public FixVec2 NearestCell(FixVec2 p)
    {
        var best = GameMap.CellCenter(Cells[0].X, Cells[0].Y);
        var bestD = (best - p).LengthSquared;
        for (var i = 1; i < Cells.Count; i++)
        {
            var c = GameMap.CellCenter(Cells[i].X, Cells[i].Y);
            var d = (c - p).LengthSquared;
            if (d < bestD) { best = c; bestD = d; }
        }
        return best;
    }
}

/// <summary>T26: the map's bridges, found once from the terrain (`=` groups, 4-connected, numbered
/// by their first cell in row-major order). Hashed only once something has happened to a bridge,
/// so maps whose bridges are never touched keep their golden hashes.</summary>
public sealed class BridgeStore
{
    /// <summary>Hit points per bridge (tuning, not canon: canon only says bridges can be destroyed).</summary>
    public static readonly Fix64 BridgeHp = Fix64.FromInt(800); // ~80 s for one MBT, ~16 s for five

    private readonly List<Bridge> _bridges = new();
    public IReadOnlyList<Bridge> Bridges => _bridges;

    public Bridge? Get(int id) => (uint)id < (uint)_bridges.Count ? _bridges[id] : null;

    /// <summary>Bridge covering this cell, or null.</summary>
    public Bridge? At(int cx, int cy)
    {
        foreach (var b in _bridges) if (b.Covers(cx, cy)) return b;
        return null;
    }

    public static BridgeStore FromMap(GameMap map)
    {
        var store = new BridgeStore();
        var seen = new bool[map.Width * map.Height];
        for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                if (seen[y * map.Width + x] || map.TerrainAt(x, y) != Terrain.Bridge) continue;
                var cells = new List<(int X, int Y)>();
                var stack = new Stack<(int X, int Y)>();
                stack.Push((x, y));
                seen[y * map.Width + x] = true;
                while (stack.Count > 0)
                {
                    var c = stack.Pop();
                    cells.Add(c);
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        var (nx, ny) = (c.X + dx, c.Y + dy);
                        if (!map.InBounds(nx, ny) || seen[ny * map.Width + nx] || map.TerrainAt(nx, ny) != Terrain.Bridge) continue;
                        seen[ny * map.Width + nx] = true;
                        stack.Push((nx, ny));
                    }
                }
                cells.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
                store._bridges.Add(new Bridge(store._bridges.Count, cells, BridgeHp));
            }
        return store;
    }

    /// <summary>Mix every bridge that is damaged or down (id, hp, broken). Untouched bridges add nothing.</summary>
    public ulong Hash(ulong h)
    {
        foreach (var b in _bridges)
        {
            if (!b.Damaged) continue;
            h = (h ^ (ulong)(uint)b.Id) * 1099511628211UL;
            h = (h ^ (ulong)b.Hp.Raw) * 1099511628211UL;
            h = (h ^ (b.Broken ? 1UL : 0UL)) * 1099511628211UL;
        }
        return h;
    }
}
