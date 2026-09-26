using Godot;

namespace Rts.Game;

/// <summary>HUD-space drag-selection rectangle for the 3D camera.</summary>
public partial class SelectionOverlay : Control
{
    private Rect2? _box;

    public Rect2? Box
    {
        get => _box;
        set
        {
            _box = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_box is not { } rect || rect.Size.LengthSquared() < 0.01f) return;
        rect = new Rect2(rect.Position, rect.Size);
        DrawRect(rect, new Color(0.45f, 0.78f, 1f, 0.15f), true);
        DrawRect(rect, new Color(0.65f, 0.88f, 1f, 0.9f), false, 1.5f);
    }
}
