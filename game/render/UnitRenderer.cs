using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>Draws units as circles from sim state (read-only), interpolating between the last two
/// tick snapshots. Also draws selection rings and the live selection box (delegated by Main).
/// Fix64 → float happens here, at the render boundary (game/CONTEXT.md).</summary>
public partial class UnitRenderer : Node2D
{
    public const float CellSize = 32.0f; // px per sim cell at zoom 1

    public UnitStore Units = null!;
    public System.Func<IReadOnlyCollection<int>>? SelectedIds;
    public System.Func<(Vector2 A, Vector2 B)?>? DragBox; // world-space, while dragging
    public System.Func<int, Vector2?>? TargetPos; // world pos of a unit id (attack lines)

    // double-buffered positions for interpolation: index = completedTick % 2
    private readonly Dictionary<int, Vector2>[] _snap = { new(), new() };
    private int _lastTick = -1;

    public static Vector2 ToWorld(Fix64 x, Fix64 y) =>
        new((float)x.ToDouble() * CellSize, (float)y.ToDouble() * CellSize);

    /// <summary>Called by Main after each sim step with the tick that just completed.</summary>
    public void CaptureSnapshot(int tick)
    {
        var buf = _snap[tick % 2];
        buf.Clear();
        foreach (var u in Units.Units)
            buf[u.Id.Value] = ToWorld(u.Position.X, u.Position.Y);
        _lastTick = tick;
    }

    public override void _Draw()
    {
        if (Units == null || _lastTick < 1) return;

        // alpha = fraction of the current frame's step budget, set by Main each frame
        var t = (float)Main.DrawAlpha;
        var prev = _snap[(_lastTick + 1) % 2]; // tick-1 snapshot lives in the other buffer
        var curr = _snap[_lastTick % 2];
        var sel = SelectedIds?.Invoke();

        foreach (var u in Units.Units)
        {
            var id = u.Id.Value;
            if (!curr.TryGetValue(id, out var p)) continue;
            var pos = prev.TryGetValue(id, out var q) ? q.Lerp(p, t) : p;
            var r = (float)u.Radius.ToDouble() * 2f * CellSize;

            DrawCircle(pos, r, id % 2 == 0 ? Colors.SteelBlue : Colors.CadetBlue);

            // HP bar when hurt
            if (u.Hp < u.MaxHp)
            {
                var frac = (float)(u.Hp.ToDouble() / u.MaxHp.ToDouble());
                var w = r * 2f;
                var top = pos - new Vector2(r, r + 5f);
                DrawRect(new Rect2(top, new Vector2(w, 3f)), new Color(0.1f, 0.1f, 0.1f, 0.8f), true);
                var barColor = frac > 0.6f ? Colors.Green : frac > 0.3f ? Colors.Yellow : Colors.Red;
                DrawRect(new Rect2(top, new Vector2(w * frac, 3f)), barColor, true);
            }

            // attack target line
            if (u.TargetId.Value >= 0 && TargetPos != null && TargetPos(u.TargetId.Value) is { } tp)
                DrawLine(pos, tp, new Color(1f, 0.3f, 0.3f, 0.6f), 1f);

            if (sel != null && sel.Contains(id))
                DrawArc(pos, r + 3f, 0, Mathf.Tau, 32, Colors.LimeGreen, 1.5f);
        }

        if (DragBox?.Invoke() is { } box)
        {
            var rect = new Rect2(box.A, box.B - box.A);
            DrawRect(rect, Colors.White with { A = 0.15f }, true);
            DrawRect(rect, Colors.White with { A = 0.9f }, false, 1f);
        }
    }
}
