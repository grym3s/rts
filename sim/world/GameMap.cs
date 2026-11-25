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
    /// <summary>T25: tile type per cell (Land unless a map says otherwise). Cliff cells are also
    /// blocked, so they stop every surface domain; air ignores both.</summary>
    private readonly Terrain[] _terrain;

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
        _terrain = new Terrain[width * height];
        Array.Fill(_structure, -1);
    }

    public bool InBounds(int cx, int cy) => (uint)cx < (uint)Width && (uint)cy < (uint)Height;
    public bool IsBlocked(int cx, int cy) => InBounds(cx, cy) && _blocked[cy * Width + cx];
    /// <summary>Open for a land unit: not blocked, land or bridge, no building.</summary>
    public bool IsWalkable(int cx, int cy) => IsWalkable(cx, cy, MoveDomain.Land);
    /// <summary>T25: open for a unit of this domain. Air needs only the map bounds; surface domains
    /// need an unblocked cell of a terrain they allow and no building footprint.</summary>
    public bool IsWalkable(int cx, int cy, MoveDomain domain)
    {
        if (!InBounds(cx, cy)) return false;
        if (domain == MoveDomain.Air) return true;
        var i = cy * Width + cx;
        return !_blocked[i] && _structure[i] < 0 && Domains.Allows(domain, _terrain[i]);
    }
    /// <summary>Terrain only (ignores buildings): what the map file says, for a land unit.</summary>
    public bool IsTerrainWalkable(int cx, int cy) => InBounds(cx, cy) && !_blocked[cy * Width + cx] && Domains.Allows(MoveDomain.Land, _terrain[cy * Width + cx]);
    /// <summary>T25: tile type; Cliff out of bounds.</summary>
    public Terrain TerrainAt(int cx, int cy) => InBounds(cx, cy) ? _terrain[cy * Width + cx] : Terrain.Cliff;
    /// <summary>T25: set a cell's tile. Cliff also blocks the cell.</summary>
    public void SetTerrain(int cx, int cy, Terrain t)
    {
        if (!InBounds(cx, cy)) throw new ArgumentOutOfRangeException(nameof(cx), $"cell ({cx},{cy}) outside {Width}x{Height}");
        _terrain[cy * Width + cx] = t;
        if (t == Terrain.Cliff) _blocked[cy * Width + cx] = true;
    }
    /// <summary>Building whose footprint covers the cell, or -1.</summary>
    public int StructureAt(int cx, int cy) => InBounds(cx, cy) ? _structure[cy * Width + cx] : -1;

    /// <summary>Mark a vein cell (T33): placement keeps buildings off ore.</summary>
    public void MarkResource(int cx, int cy) { if (InBounds(cx, cy)) _resource[cy * Width + cx] = true; }
    public bool IsResource(int cx, int cy) => InBounds(cx, cy) && _resource[cy * Width + cx];

    /// <summary>True if every cell of the rect is in bounds, unblocked, free of buildings, holds
    /// no vein and is of the ground the building stands on: Land (not a bridge) for ordinary
    /// buildings, Water for `onWater` ones (T25: shipyards, offshore platforms).</summary>
    public bool CanOccupy(int x0, int y0, int w, int h, bool onWater = false)
    {
        var need = onWater ? Terrain.Water : Terrain.Land;
        for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
            {
                if (!InBounds(x, y)) return false;
                var i = y * Width + x;
                if (_blocked[i] || _structure[i] >= 0 || _resource[i] || _terrain[i] != need) return false;
            }
        return true;
    }

    /// <summary>T26: a bridge stands (cells are Bridge) or has fallen (cells are Water: ships pass,
    /// ground units don't). Bumps StructureEpoch so paths across it re-check.</summary>
    public void SetBridgeIntact(IEnumerable<(int X, int Y)> cells, bool intact, Terrain standing = Terrain.Bridge)
    {
        foreach (var (x, y) in cells) _terrain[y * Width + x] = intact ? standing : Terrain.Water;
        StructureEpoch++;
    }

    /// <summary>T26: no building may cover this cell (causeways: they can fall into the sea).</summary>
    public void MarkNoBuild(int cx, int cy) { if (InBounds(cx, cy)) _resource[cy * Width + cx] = true; }

    /// <summary>T25: every cell of the rect is in bounds and of terrain `t`.</summary>
    public bool AllTerrain(int x0, int y0, int w, int h, Terrain t)
    {
        for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
                if (!InBounds(x, y) || _terrain[y * Width + x] != t) return false;
        return true;
    }

    /// <summary>T25 shore rule: some cell in the ring just outside the rect (8-neighbourhood) is
    /// of terrain `t`. A shipyard on water needs Land beside it; a coastal gun on land needs Water.</summary>
    public bool RingTouches(int x0, int y0, int w, int h, Terrain t)
    {
        for (var y = y0 - 1; y <= y0 + h; y++)
            for (var x = x0 - 1; x <= x0 + w; x++)
            {
                if (x >= x0 && x < x0 + w && y >= y0 && y < y0 + h) continue;
                if (InBounds(x, y) && _terrain[y * Width + x] == t) return true;
            }
        return false;
    }

    /// <summary>T25: build a map from a scenario's `map` object: `width`, `height`, optional
    /// `terrain` (rows of `.~#=`, row 0 = y 0) and optional `blocked` rects [x0,y0,x1,y1].
    /// Shared by tools/scenario and tools/sim-host so both read maps the same way.</summary>
    public static GameMap FromJson(System.Text.Json.JsonElement mapEl)
    {
        var map = new GameMap(mapEl.GetProperty("width").GetInt32(), mapEl.GetProperty("height").GetInt32());
        if (mapEl.TryGetProperty("terrain", out var rows))
        {
            var y = 0;
            foreach (var row in rows.EnumerateArray())
            {
                var line = row.GetString()!;
                if (line.Length != map.Width || y >= map.Height)
                    throw new InvalidDataException($"terrain row {y} is {line.Length} wide, map is {map.Width}x{map.Height}");
                for (var x = 0; x < line.Length; x++) map.SetTerrain(x, y, Domains.TileOf(line[x]));
                y++;
            }
            if (y != map.Height) throw new InvalidDataException($"terrain has {y} rows, map is {map.Height} high");
        }
        if (mapEl.TryGetProperty("blocked", out var blocked))
            foreach (var r in blocked.EnumerateArray())
                map.BlockRect(r[0].GetInt32(), r[1].GetInt32(), r[2].GetInt32(), r[3].GetInt32());
        return map;
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
