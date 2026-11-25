using Rts.Sim.Core;

namespace Rts.Sim.World;

/// <summary>
/// Square-cell grid world. Cell (0,0) is at the origin; cell (cx,cy) spans
/// [cx,cx+1] x [cy,cy+1] in world units. Coordinates stay Fix64 end to end;
/// cell indices are exact ints derived by flooring.
/// </summary>
public sealed class GameMap
{
    public int Width { get; }
    public int Height { get; }
    private readonly bool[] _blocked;
    /// <summary>T31: cells covered by a building footprint (separate from terrain so a destroyed
    /// building never opens up a cliff). Owner entity id, or -1.</summary>
    private readonly int[] _structure;
    private readonly SortedDictionary<int, (int X, int Y, int W, int H)> _footprints = new();
    /// <summary>T33: cells holding a resource vein. Walkable, but no building may cover one.</summary>
    private readonly bool[] _resource;

    /// <summary>Bumped whenever a footprint is placed or released; navigation re-checks paths
    /// planned under an older epoch (T31). Not hashed: footprints follow from building units.</summary>
    public int StructureEpoch { get; private set; }

    public GameMap(int width, int height)
    {
        Width = width;
        Height = height;
        _blocked = new bool[width * height];
        _structure = new int[width * height];
        _resource = new bool[width * height];
        Array.Fill(_structure, -1);
    }

    public bool InBounds(int cx, int cy) => (uint)cx < (uint)Width && (uint)cy < (uint)Height;
    public bool IsBlocked(int cx, int cy) => InBounds(cx, cy) && _blocked[cy * Width + cx];
    public bool IsWalkable(int cx, int cy) => InBounds(cx, cy) && !_blocked[cy * Width + cx] && _structure[cy * Width + cx] < 0;
    /// <summary>Terrain only (ignores buildings): what the map file says.</summary>
    public bool IsTerrainWalkable(int cx, int cy) => InBounds(cx, cy) && !_blocked[cy * Width + cx];
    /// <summary>Building whose footprint covers the cell, or -1.</summary>
    public int StructureAt(int cx, int cy) => InBounds(cx, cy) ? _structure[cy * Width + cx] : -1;

    /// <summary>Mark a vein cell (T33): placement keeps buildings off ore.</summary>
    public void MarkResource(int cx, int cy) { if (InBounds(cx, cy)) _resource[cy * Width + cx] = true; }
    public bool IsResource(int cx, int cy) => InBounds(cx, cy) && _resource[cy * Width + cx];

    /// <summary>True if every cell of the rect is in bounds, terrain-walkable, free of buildings
    /// and holds no vein.</summary>
    public bool CanOccupy(int x0, int y0, int w, int h)
    {
        for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
                if (!IsWalkable(x, y) || _resource[y * Width + x]) return false;
        return true;
    }

    /// <summary>Mark a building's footprint (caller checked CanOccupy).</summary>
    public void OccupyStructure(int ownerId, int x0, int y0, int w, int h)
    {
        for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
                _structure[y * Width + x] = ownerId;
        _footprints[ownerId] = (x0, y0, w, h);
        StructureEpoch++;
    }

    /// <summary>Free the footprints of owners that are gone (destroyed buildings). Ascending owner
    /// id order; returns how many were released.</summary>
    public int ReleaseStructures(Func<int, bool> gone)
    {
        var freed = 0;
        foreach (var (owner, r) in _footprints.ToList())
        {
            if (!gone(owner)) continue;
            for (var y = r.Y; y < r.Y + r.H; y++)
                for (var x = r.X; x < r.X + r.W; x++)
                    _structure[y * Width + x] = -1;
            _footprints.Remove(owner);
            freed++;
        }
        if (freed > 0) StructureEpoch++;
        return freed;
    }

    public bool TryGetFootprint(int ownerId, out (int X, int Y, int W, int H) rect) => _footprints.TryGetValue(ownerId, out rect);

    public void Block(int cx, int cy)
    {
        if (!InBounds(cx, cy)) throw new ArgumentOutOfRangeException(nameof(cx), $"cell ({cx},{cy}) outside {Width}x{Height}");
        _blocked[cy * Width + cx] = true;
    }

    public void BlockRect(int x0, int y0, int x1, int y1)
    {
        for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
                Block(x, y);
    }

    public static int CellOf(Fix64 v) => v.ToIntFloor();
    public static FixVec2 CellCenter(int cx, int cy) =>
        new(Fix64.FromInt(cx) + Fix64.Half, Fix64.FromInt(cy) + Fix64.Half);
}
