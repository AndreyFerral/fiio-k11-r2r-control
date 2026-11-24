using K11Control.Config;

namespace K11Control.UI;

public sealed class HotkeyCaptureBox : TextBox
{
    private HotkeyBinding _binding = new();

    public HotkeyCaptureBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        BackColor = Theme.BgInput;
        ForeColor = Theme.TextPrimary;
        BorderStyle = BorderStyle.FixedSingle;
        Font = Theme.FontMono;
        TextAlign = HorizontalAlignment.Center;
        Text = "(None)";
    }

    public HotkeyBinding Binding
    {
        get => new() { Modifiers = _binding.Modifiers, Key = _binding.Key };
        set
        {
            _binding = new HotkeyBinding { Modifiers = value.Modifiers, Key = value.Key };
            Text = _binding.ToString();
            ForeColor = _binding.IsConfigured ? Theme.Accent : Theme.TextMuted;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys keyCode = keyData & Keys.KeyCode;
        if (keyCode is Keys.Tab or Keys.Return)
            return base.ProcessCmdKey(ref msg, keyData);

        if (keyCode is Keys.Escape or Keys.Back or Keys.Delete)
        {
            Binding = new HotkeyBinding();
            return true;
        }

        // Ignore pure modifier presses until an actual key accompanies them.
        if (keyCode is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu
            or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LWin or Keys.RWin or Keys.None)
        {
            return true;
        }

        HotkeyModifiers mods = HotkeyModifiers.None;
        if (keyData.HasFlag(Keys.Control)) mods |= HotkeyModifiers.Control;
        if (keyData.HasFlag(Keys.Alt)) mods |= HotkeyModifiers.Alt;
        if (keyData.HasFlag(Keys.Shift)) mods |= HotkeyModifiers.Shift;

        Binding = new HotkeyBinding { Modifiers = mods, Key = keyCode };
        return true;
    }
}
