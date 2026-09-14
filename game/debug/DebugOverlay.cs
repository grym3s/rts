using Godot;

namespace Rts.Game;

/// <summary>F1 toggles: tick, sim ms/tick, selected count + ids, and paths of selected units
/// (drawn by Main from sim orders). Pure read overlay.</summary>
public partial class DebugOverlay : CanvasLayer
{
    private readonly Label _label = new();
    public bool Enabled { get; private set; }

    public override void _Ready()
    {
        _label.Position = new Vector2(8, 8);
        _label.AddThemeColorOverride("font_color", Colors.Yellow);
        _label.AddThemeConstantOverride("font_size", 12);
        AddChild(_label);
    }

    public override void _UnhandledKeyInput(Godot.InputEvent @event)
    {
        if (@event is InputEventKey { Keycode: Key.F1, Pressed: true })
        {
            Enabled = !Enabled;
            _label.Visible = Enabled;
        }
    }

    public void SetText(string text)
    {
        if (Enabled) _label.Text = text;
    }
}
