using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>Tick-step 0 (outside the sim): mouse/keyboard → selection changes and Commands.
/// Emits into Main's outbox; never touches sim state directly. Camera world coords via the viewport canvas transform.</summary>
public partial class OrdersInput : Node2D
{
    public required Func<IReadOnlyCollection<int>> SelectedIds;
    public required SelectionController Selection;
    public required UnitStore Units;
    public required Action<Command> Emit;
    public required Func<int> CurrentTick;
    public const int Faction = 0; // single-player slice: local player

    private Vector2 _dragStart;
    private bool _dragging;

    public Action<Vector2, Vector2>? DragBoxChanged; // world coords for the renderer, null on end

    public override void _UnhandledInput(Godot.InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } lb:
                if (lb.Pressed) OnSelectPress(lb.Position, lb.ShiftPressed);
                else OnSelectRelease(lb.Position, lb.ShiftPressed);
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } rb:
                OnOrderPress(rb.Position, rb.ShiftPressed);
                break;

            case InputEventKey { Pressed: true } k:
                OnKey(k.Keycode, k.CtrlPressed, k.ShiftPressed);
                break;
        }
    }

    private void OnSelectPress(Vector2 screen, bool shift)
    {
        _dragStart = screen;
        _dragging = false;
        _ = shift;
    }

    private void OnSelectRelease(Vector2 screen, bool shift)
    {
        var world = ToWorld(screen);
        if (!_dragging && screen.DistanceTo(_dragStart) <= SelectionController.BoxDragThreshold)
        {
            var hit = HitTest(world);
            if (hit.HasValue) Selection.BeginClick(hit.Value, shift);
            else if (!shift) Selection.BoxSelect(Array.Empty<int>(), false); // click on empty ground clears
        }
        else
        {
            var a = ToWorld(_dragStart);
            var ids = Units.Units
                .Where(u => Within(WorldToScreen(u.Position), a, world))
                .Select(u => u.Id.Value);
            Selection.BoxSelect(ids, shift);
        }
        DragBoxChanged?.Invoke(world, world); // signal end (Main clears its box state)
    }

    public override void _Process(double delta)
    {
        // track drag past threshold (motion events alone would miss diagonal intent)
        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            var s = GetViewport().GetMousePosition();
            if (!_dragging && s.DistanceTo(_dragStart) > SelectionController.BoxDragThreshold)
                _dragging = true;
            if (_dragging)
                DragBoxChanged?.Invoke(ToWorld(_dragStart), ToWorld(s));
        }
    }

    private void OnOrderPress(Vector2 screen, bool shiftQueue)
    {
        var ids = SelectedIds();
        if (ids.Count == 0) return;
        var units = ids.Select(i => new EntityId(i)).ToArray();
        var target = ToFix(screen);
        var tick = CurrentTick();

        var attackMove = Input.IsKeyPressed(Key.A);
        Emit(attackMove
            ? new AttackMoveCommand(tick, Faction, units, target, shiftQueue)
            : new MoveCommand(tick, Faction, units, target, shiftQueue));
    }

    private void OnKey(Key key, bool ctrl, bool shift)
    {
        // control groups 1–3 (ASCII digits map straight onto Key values)
        if (key >= (Key)'1' && key <= (Key)'3')
        {
            var g = (int)key - (int)'1';
            if (ctrl) Selection.AssignGroup(g);
            else
            {
                var ids = Selection.GetGroup(g);
                Selection.BoxSelect(ids, shift);
            }
            return;
        }

        if (key == Key.S)
        {
            var ids = SelectedIds();
            if (ids.Count > 0)
                Emit(new StopCommand(CurrentTick(), Faction, ids.Select(i => new EntityId(i)).ToArray()));
        }
    }

    private int? HitTest(Vector2 world)
    {
        // topmost = last spawned wins, matching draw order
        for (var i = Units.Units.Count - 1; i >= 0; i--)
        {
            var u = Units.Units[i];
            var p = UnitRenderer.ToWorld(u.Position.X, u.Position.Y);
            var r = (float)u.Radius.ToDouble() * 2f * UnitRenderer.CellSize + 2f;
            if (p.DistanceTo(world) <= r) return u.Id.Value;
        }
        return null;
    }

    private static bool Within(Vector2 p, Vector2 a, Vector2 b) =>
        p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) &&
        p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);

    private Vector2 ToWorld(Vector2 screen) => GetViewport().GetCanvasTransform().AffineInverse() * screen;
    private Vector2 WorldToScreen(FixVec2 p) => GetViewport().GetCanvasTransform() * UnitRenderer.ToWorld(p.X, p.Y);
    private FixVec2 ToFix(Vector2 screen)
    {
        var w = ToWorld(screen);
        return new FixVec2(Fix64.FromDouble(w.X / UnitRenderer.CellSize), Fix64.FromDouble(w.Y / UnitRenderer.CellSize));
    }
}
