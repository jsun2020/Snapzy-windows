using System.Windows.Input;
using Snapzy.Core.Hotkeys;
using TextBox = System.Windows.Controls.TextBox;

namespace Snapzy.App.SettingsUI;

/// <summary>Read-only text box that records the key combo pressed while focused.</summary>
public class HotkeyCaptureBox : TextBox
{
    public event Action? GestureChanged;

    public HotkeyCaptureBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        InputMethod.SetIsInputMethodEnabled(this, false);
    }

    public string Gesture
    {
        get => Text;
        set => Text = value;
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None)
            return;

        var name = KeyName(key);
        if (name is null) return;

        var parts = new List<string>(4);
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(name);

        var gesture = string.Join("+", parts);
        if (!HotkeyCombo.TryParse(gesture, out var combo)) return;
        Text = combo.ToString();
        GestureChanged?.Invoke();
    }

    private static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => key.ToString()[1..],
        >= Key.NumPad0 and <= Key.NumPad9 => key.ToString()[6..],
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.PrintScreen => "PrintScreen",
        Key.Space => "Space",
        Key.Tab => "Tab",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Insert => "Insert",
        Key.Delete => "Delete",
        Key.Up => "Up",
        Key.Down => "Down",
        Key.Left => "Left",
        Key.Right => "Right",
        _ => null,
    };
}
