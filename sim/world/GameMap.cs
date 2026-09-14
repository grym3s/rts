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

    public GameMap(int width, int height)
    {
        Width = width;
        Height = height;
        _blocked = new bool[width * height];
    }

    public bool InBounds(int cx, int cy) => (uint)cx < (uint)Width && (uint)cy < (uint)Height;
    public bool IsBlocked(int cx, int cy) => InBounds(cx, cy) && _blocked[cy * Width + cx];
    public bool IsWalkable(int cx, int cy) => InBounds(cx, cy) && !_blocked[cy * Width + cx];

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
