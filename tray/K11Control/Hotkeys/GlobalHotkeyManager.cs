using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using K11Control.Config;

namespace K11Control.Hotkeys;

public enum HotkeyActionId
{
    ToggleOutput = 1,
    ToggleCustomText = 2,
    ToggleRgb = 3,
    RebootDac = 4,
    SwitchToPo = 5,
    SwitchToPre = 6,
    ToggleMute = 7,
    CycleGain = 8,
    ToggleFilter = 9
}

[SupportedOSPlatform("windows")]
public sealed class GlobalHotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly HotkeyMessageWindow _window;
    private readonly List<int> _registeredIds = [];

    public event Action<HotkeyActionId>? HotkeyPressed;

    public GlobalHotkeyManager()
    {
        _window = new HotkeyMessageWindow(id => HotkeyPressed?.Invoke((HotkeyActionId)id));
    }

    public IReadOnlyList<string> ApplyBindings(HotkeyConfig config)
    {
        UnregisterAll();
        var errors = new List<string>();

        TryRegister(HotkeyActionId.ToggleOutput, config.ToggleOutput, "Универсальный Toggle (PO <-> PRE)", errors);
        TryRegister(HotkeyActionId.SwitchToPre, config.SwitchToPre, "Колонки (PRE)", errors);
        TryRegister(HotkeyActionId.SwitchToPo, config.SwitchToPo, "Наушники (PO)", errors);
        TryRegister(HotkeyActionId.ToggleCustomText, config.ToggleCustomText, "Экран (Обычный / Кастомный)", errors);
        TryRegister(HotkeyActionId.ToggleRgb, config.ToggleRgb, "Подсветка (On / Custom / Off)", errors);
        TryRegister(HotkeyActionId.ToggleMute, config.ToggleMute, "Переключить звук (Mute)", errors);
        TryRegister(HotkeyActionId.CycleGain, config.CycleGain, "Усиление (L -> M -> H)", errors);
        TryRegister(HotkeyActionId.ToggleFilter, config.ToggleFilter, "Фильтр ЦАПа (OS / NOS)", errors);
        TryRegister(HotkeyActionId.RebootDac, config.RebootDac, "Перезагрузить ЦАП", errors);
        return errors;
    }

    private void TryRegister(HotkeyActionId id, HotkeyBinding binding, string label, List<string> errors)
    {
        if (!binding.IsConfigured)
            return;

        uint mods = (uint)(binding.Modifiers | HotkeyModifiers.NoRepeat);
        uint vk = (uint)binding.Key;
        if (!RegisterHotKey(_window.Handle, (int)id, mods, vk))
        {
            errors.Add($"{label} ({binding}): Win32 error {Marshal.GetLastWin32Error()}");
            return;
        }
        _registeredIds.Add((int)id);
    }

    public void UnregisterAll()
    {
        foreach (int id in _registeredIds)
            _ = UnregisterHotKey(_window.Handle, id);
        _registeredIds.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.DestroyHandle();
    }

    private sealed class HotkeyMessageWindow : NativeWindow
    {
        private readonly Action<int> _onHotkey;

        public HotkeyMessageWindow(Action<int> onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey)
                _onHotkey(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
