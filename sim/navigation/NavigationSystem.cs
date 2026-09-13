using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Navigation;

/// <summary>Grid A* (8-dir, integer octile heuristic). Deterministic: fixed neighbour order, no floats.</summary>
public static class Pathfinding
{
    private static readonly int[] Dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] Dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

    /// <summary>Cell centres from start (exclusive) to goal (inclusive); null when unreachable.
    /// The start cell is treated as walkable even if blocked (unit standing on a blocked cell must still move out).</summary>
    public static List<FixVec2>? FindPath(GameMap map, (int x, int y) start, (int x, int y) goal)
    {
        if (!map.InBounds(goal.x, goal.y) || map.IsBlocked(goal.x, goal.y)) return null;
        if (start == goal) return new List<FixVec2>();

        var w = map.Width;
        var size = w * map.Height;
        var g = new int[size];       // cost << 10, fixed point
        var cameFrom = new int[size];
        var closed = new bool[size];
        for (var i = 0; i < size; i++) { g[i] = int.MaxValue; cameFrom[i] = -1; }

        // simple binary heap on (f, cell); ties broken by cell id => fully deterministic
        var heap = new Heap(size);
        var si = start.y * w + start.x;
        var gi = goal.y * w + goal.x;
        g[si] = 0;
        heap.Push(Guess(start, goal), si);

        while (heap.Count > 0)
        {
            var (_, ci) = heap.Pop();
            if (closed[ci]) continue;
            closed[ci] = true;
            if (ci == gi) return Reconstruct(map, cameFrom, ci, w, start);

            var cx = ci % w;
            var cy = ci / w;
            for (var n = 0; n < 8; n++)
            {
                var nx = cx + Dx[n];
                var ny = cy + Dy[n];
                var isStart = nx == start.x && ny == start.y;
                if (!map.IsWalkable(nx, ny) && !isStart) continue;
                // no corner cutting: a diagonal needs both shared side cells open (start cell exempt)
                if ((n & 1) == 1)
                {
                    var sideOpenX = map.IsWalkable(cx + Dx[n], cy) || (cx + Dx[n] == start.x && cy == start.y);
                    var sideOpenY = map.IsWalkable(cx, cy + Dy[n]) || (cx == start.x && cy + Dy[n] == start.y);
                    if (!sideOpenX || !sideOpenY) continue;
                }

                var ni = ny * w + nx;
                if (closed[ni]) continue;
                var step = (n & 1) == 1 ? 1448 : 1024; // √2<<10 vs 1<<10
                var ng = g[ci] + step;
                if (ng < g[ni])
                {
                    g[ni] = ng;
                    cameFrom[ni] = ci;
                    heap.Push(ng + Guess((nx, ny), goal), ni);
                }
            }
        }
        return null;
    }

    private static List<FixVec2> Reconstruct(GameMap map, int[] cameFrom, int goalIdx, int w, (int x, int y) start)
    {
        var cells = new List<(int x, int y)>();
        for (var ci = goalIdx; ci != -1 && !(ci / w == start.y && ci % w == start.x); ci = cameFrom[ci])
            cells.Add((ci % w, ci / w));
        cells.Reverse();
        var path = new List<FixVec2>(cells.Count);
        foreach (var (x, y) in cells) path.Add(GameMap.CellCenter(x, y));
        return path;
    }

    private static int Guess((int x, int y) a, (int x, int y) b)
    {
        var dx = Math.Abs(a.x - b.x);
        var dy = Math.Abs(a.y - b.y);
        return 1024 * (dx + dy) + 424 * Math.Min(dx, dy); // (√2−1)<<10 ≈ 0.4142<<10
    }

    /// <summary>Min-heap keyed by (f, cell). Deterministic pop order.</summary>
    private sealed class Heap
    {
        private (int f, int cell)[] _a;
        private int _n;
        public Heap(int cap) => _a = new (int, int)[cap + 1];
        public int Count => _n;

        public void Push(int f, int cell)
        {
            if (_n + 1 == _a.Length) Array.Resize(ref _a, _a.Length * 2); // duplicate pushes can exceed cell count
            var i = ++_n;
            while (i > 1 && Greater((_a[i / 2]), (f, cell))) { _a[i] = _a[i / 2]; i /= 2; }
            _a[i] = (f, cell);
        }

        public (int f, int cell) Pop()
        {
            var top = _a[1];
            var last = _a[_n--];
            if (_n > 0)
            {
                var i = 1;
                while (true)
                {
                    var c = i * 2;
                    if (c > _n) break;
                    if (c + 1 <= _n && Greater(_a[c], _a[c + 1])) c++;
                    if (!Greater(last, _a[c])) break;
                    _a[i] = _a[c];
                    i = c;
                }
                _a[i] = last;
            }
            return top;
        }

        private static bool Greater((int f, int cell) x, (int f, int cell) y) =>
            x.f != y.f ? x.f > y.f : x.cell > y.cell;
    }
}

/// <summary>Tick step 3 (see ../../CONTEXT.md): plan missing paths, steer along them, separate, integrate.</summary>
public static class NavigationSystem
{
    public const int PlanBudgetPerTick = 25; // cells explored? no: full A* runs per tick, round-robin; bench will tell

    /// <summary>Advance orders → movement for one tick. Deterministic: units iterated in store order (spawn order).</summary>
    public static void Step(GameMap map, UnitStore units, Dictionary<EntityId, MoveOrder> orders, int planBudget = PlanBudgetPerTick)
    {
        // 1. plan: every unit whose order has no path yet (ordered => deterministic budget)
        var budget = planBudget;
        foreach (var u in units.Units)
        {
            if (budget <= 0) break;
            if (orders.TryGetValue(u.Id, out var o) && o.Path.Count == 0)
            {
                var goalCell = (GameMap.CellOf(o.Target.X), GameMap.CellOf(o.Target.Y));
                var startCell = (GameMap.CellOf(u.Position.X), GameMap.CellOf(u.Position.Y));
                var path = Pathfinding.FindPath(map, startCell, goalCell);
                if (path == null) { Console.Error.WriteLine($"UNREACHABLE u={u.Id.Value}"); orders.Remove(u.Id); continue; }
                if (path.Count == 0) { Console.Error.WriteLine($"EMPTYPATH u={u.Id.Value}"); orders.Remove(u.Id); continue; }
                // goal cell reached means the raw target point, not its centre: append exact target
                path[path.Count - 1] = o.Target;
                o.Path = path;
                o.Waypoint = 0;
                budget--;
            }
        }

        // 2. desired velocities toward current waypoint
        foreach (var u in units.Units)
        {
            u.Velocity = FixVec2.Zero;
            if (!orders.TryGetValue(u.Id, out var o)) continue;
            if (o.Path.Count == 0) continue; // awaiting path (plan budget); not an arrival
            var wp = o.CurrentWaypoint;
            if (wp == null) { orders.Remove(u.Id); continue; }

            var delta = wp.Value - u.Position;
            var dist = delta.Length;
            var arrive = Fix64.Ratio(1, 4); // stop within 0.25 of a waypoint/target
            if (dist <= arrive)
            {
                o.Waypoint++;
                if (o.Waypoint >= o.Path.Count) orders.Remove(u.Id); // arrived
                continue;
            }
            var stepSpeed = Fix64.Min(u.Speed, dist * Fix64.FromInt(SimWorld.TicksPerSecond));
            u.Velocity = delta.Normalized() * stepSpeed;
        }

        // 3. separation: push overlapping pairs apart along the contact normal (each pair once).
        // A pushed unit must land in open space: if one side's slot is blocked, the other takes the full push.
        var list = units.Units;
        for (var i = 0; i < list.Count; i++)
        {
            for (var j = i + 1; j < list.Count; j++)
            {
                var a = list[i];
                var b = list[j];
                var delta = b.Position - a.Position;
                var minDist = a.Radius + b.Radius;
                var d2 = delta.LengthSquared;
                if (d2.Raw == 0)
                {
                    // perfectly coincident (rare): deterministic kick along +X to whichever side is open
                    if (IsPointWalkable(map, a.Position + new FixVec2(Fix64.Ratio(1, 16), Fix64.Zero), a.Radius))
                        a.Position += new FixVec2(Fix64.Ratio(1, 16), Fix64.Zero);
                    if (IsPointWalkable(map, b.Position + new FixVec2(Fix64.Ratio(1, 16), Fix64.Zero), b.Radius))
                        b.Position += new FixVec2(Fix64.Ratio(1, 16), Fix64.Zero);
                    continue;
                }
                var min2 = minDist * minDist;
                if (d2 >= min2) continue;
                var d = Fix64.Sqrt(d2);
                var push = (minDist - d) * Fix64.Half;
                var dir = delta.Normalized();
                var aNext = a.Position - dir * push;
                var bNext = b.Position + dir * push;
                var aOk = IsPointWalkable(map, aNext, a.Radius);
                var bOk = IsPointWalkable(map, bNext, b.Radius);
                if (aOk) a.Position = aNext; else push *= 2; // wall absorbs a's share
                if (bOk) b.Position = bNext;
            }
        }

        // 4. integrate + wall slide
        foreach (var u in units.Units)
        {
            if (u.Velocity == FixVec2.Zero) continue;
            var dt = Fix64.Ratio(1, SimWorld.TicksPerSecond);
            var next = u.Position + u.Velocity * dt;
            // slide: reject per axis against blocked cells (a unit's point must stay in walkable cells)
            if (IsPointWalkable(map, next, u.Radius)) u.Position = next;
            else
            {
                var nextX = new FixVec2(next.X, u.Position.Y);
                if (IsPointWalkable(map, nextX, u.Radius)) { u.Position = nextX; u.Velocity = new FixVec2(u.Velocity.X, Fix64.Zero); }
                else
                {
                    var nextY = new FixVec2(u.Position.X, next.Y);
                    if (IsPointWalkable(map, nextY, u.Radius)) { u.Position = nextY; u.Velocity = new FixVec2(Fix64.Zero, u.Velocity.Y); }
                }
            }
        }
    }

    private static bool IsPointWalkable(GameMap map, FixVec2 p, Fix64 radius)
    {
        // conservative: check the four corners of the unit's bounding box
        var r = radius;
        return map.IsWalkable(GameMap.CellOf(p.X - r), GameMap.CellOf(p.Y - r)) &&
               map.IsWalkable(GameMap.CellOf(p.X + r), GameMap.CellOf(p.Y - r)) &&
               map.IsWalkable(GameMap.CellOf(p.X - r), GameMap.CellOf(p.Y + r)) &&
               map.IsWalkable(GameMap.CellOf(p.X + r), GameMap.CellOf(p.Y + r));
    }
}
