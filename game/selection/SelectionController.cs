using System;
using System.Collections.Generic;
using Godot;

namespace Rts.Game;

/// <summary>Selection state + control groups (1–3). Pure of sim types: it stores EntityId values (ints)
/// and is fed positions by the owner; click vs box is decided by drag distance.</summary>
public sealed class SelectionController
{
    public const int ControlGroupCount = 3;
    private readonly HashSet<int> _selected = new();
    private readonly HashSet<int>[] _groups =
    {
        new(), new(), new(),
    };

    public IReadOnlyCollection<int> Selected => _selected;

    /// <summary>A drag counts as a box (instead of a click) past this screen distance (px).</summary>
    public const float BoxDragThreshold = 6.0f;

    /// <summary>Click on a unit: shift toggles membership; plain click selects only it.</summary>
    public void BeginClick(int unitId, bool shift)
    {
        if (shift)
        {
            if (!_selected.Remove(unitId)) _selected.Add(unitId);
            return;
        }
        _selected.Clear();
        _selected.Add(unitId);
    }

    /// <summary>Box select: replace (or shift-add) selection with every unit id in the rect.</summary>
    public void BoxSelect(IEnumerable<int> idsInRect, bool shift)
    {
        if (!shift) _selected.Clear();
        foreach (var id in idsInRect) _selected.Add(id);
    }

    public void AssignGroup(int group) => _groups[group] = new HashSet<int>(_selected);
    public IReadOnlyCollection<int> GetGroup(int group) => _groups[group];

    /// <summary>Drop ids that no longer exist (unit deaths later).</summary>
    public void Prune(Func<int, bool> stillExists) => _selected.RemoveWhere(id => !stillExists(id));
}
