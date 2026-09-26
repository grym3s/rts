using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>Mouse/keyboard intent for the 3D battlefield. Emits Commands only; never changes sim state.</summary>
public partial class OrdersInput : Node
{
    public required Func<IReadOnlyCollection<int>> SelectedIds;
    public required SelectionController Selection;
    public required UnitStore Units;
    public required RtsCamera Camera;
    public required Action<Command> Emit;
    public required Func<int> CurrentTick;
    public required Func<int, bool> EnemiesOf;
    public const int Faction = 0;

    private Vector2 _dragStart;
    private bool _dragging;
    public Action<Vector2, Vector2>? DragBoxChanged; // screen-space rectangle for the HUD overlay

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } left:
                if (left.Pressed) OnSelectPress(left.Position);
                else OnSelectRelease(left.Position, left.ShiftPressed);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } right:
                OnOrderPress(right.Position, right.ShiftPressed);
                break;
            case InputEventKey { Pressed: true } key:
                OnKey(key.Keycode, key.CtrlPressed, key.ShiftPressed);
                break;
        }
    }

    private void OnSelectPress(Vector2 screen)
    {
        _dragStart = screen;
        _dragging = false;
    }

    private void OnSelectRelease(Vector2 screen, bool shift)
    {
        if (!_dragging && screen.DistanceTo(_dragStart) <= SelectionController.BoxDragThreshold)
        {
            var hit = HitTest(screen);
            if (hit.HasValue) Selection.BeginClick(hit.Value, shift);
            else if (Camera.GroundPointAt(screen).HasValue && !shift)
                Selection.BoxSelect(Array.Empty<int>(), false);
        }
        else
        {
            var rect = MakeRect(_dragStart, screen);
            var ids = Units.Units
                .Where(unit => rect.HasPoint(UnitScreenPosition(unit)))
                .Select(unit => unit.Id.Value);
            Selection.BoxSelect(ids, shift);
        }
        DragBoxChanged?.Invoke(screen, screen);
    }

    public override void _Process(double delta)
    {
        if (!Input.IsMouseButtonPressed(MouseButton.Left)) return;
        var screen = GetViewport().GetMousePosition();
        if (!_dragging && screen.DistanceTo(_dragStart) > SelectionController.BoxDragThreshold)
            _dragging = true;
        if (_dragging) DragBoxChanged?.Invoke(_dragStart, screen);
    }

    private void OnOrderPress(Vector2 screen, bool shiftQueue)
    {
        if (SelectedIds().Count == 0 || Camera.GroundPointAt(screen) is not { } ground) return;
        var selected = SelectedIds().Select(id => new EntityId(id)).ToArray();
        var tick = CurrentTick();
        if (HitTest(screen) is { } hit && EnemiesOf(hit))
        {
            Emit(new AttackCommand(tick, Faction, selected, new EntityId(hit), shiftQueue));
            return;
        }

        var target = new FixVec2(Fix64.FromDouble(ground.X), Fix64.FromDouble(ground.Z));
        Emit(Input.IsKeyPressed(Key.A)
            ? new AttackMoveCommand(tick, Faction, selected, target, shiftQueue)
            : new MoveCommand(tick, Faction, selected, target, shiftQueue));
    }

    private void OnKey(Key key, bool ctrl, bool shift)
    {
        if (key >= (Key)'1' && key <= (Key)'3')
        {
            var group = (int)key - (int)'1';
            if (ctrl) Selection.AssignGroup(group);
            else Selection.BoxSelect(Selection.GetGroup(group), shift);
            return;
        }

        if (key == Key.S)
        {
            var selected = SelectedIds();
            if (selected.Count > 0)
                Emit(new StopCommand(CurrentTick(), Faction, selected.Select(id => new EntityId(id)).ToArray()));
        }
    }

    private int? HitTest(Vector2 screen)
    {
        for (var i = Units.Units.Count - 1; i >= 0; i--)
        {
            var unit = Units.Units[i];
            var center = UnitScreenPosition(unit);
            var world = UnitRenderer.ToWorld(unit.Position.X, unit.Position.Y) + Vector3.Up * 0.75f;
            var edge = Camera.UnprojectPosition(world + Camera.GlobalBasis.X * Mathf.Max(0.32f, (float)unit.Radius.ToDouble()));
            var pickRadius = Mathf.Max(9.0f, center.DistanceTo(edge) + 4.0f);
            if (screen.DistanceTo(center) <= pickRadius) return unit.Id.Value;
        }
        return null;
    }

    private Vector2 UnitScreenPosition(Rts.Sim.World.Unit unit)
    {
        var world = UnitRenderer.ToWorld(unit.Position.X, unit.Position.Y) + Vector3.Up * 0.75f;
        return Camera.UnprojectPosition(world);
    }

    private static Rect2 MakeRect(Vector2 a, Vector2 b) =>
        new(new Vector2(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y)),
            new Vector2(Mathf.Abs(b.X - a.X), Mathf.Abs(b.Y - a.Y)));
}
