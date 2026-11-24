using System.Runtime.Versioning;
using K11Control.Audio;
using K11Control.Config;
using K11Control.Hotkeys;
using K11Control.Protocol;
using K11Control.Usb;
using Microsoft.Win32;

namespace K11Control.UI;

[SupportedOSPlatform("windows")]
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly GlobalHotkeyManager _hotkeys;
    private readonly CoreAudioVolumeService _audio = new();
    private readonly DacCommandClient _dac;
    private readonly K11HidMonitor _hid = new();
    private readonly SynchronizationContext? _uiSync;
    private readonly System.Windows.Forms.Timer _pollTimer;

    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _outputToggleItem;
    private readonly ToolStripMenuItem _outputPreItem;
    private readonly ToolStripMenuItem _outputPoItem;
    private readonly ToolStripMenuItem _gainMenuItem;
    private readonly ToolStripMenuItem _gainLowItem;
    private readonly ToolStripMenuItem _gainMidItem;
    private readonly ToolStripMenuItem _gainHighItem;
    private readonly ToolStripMenuItem _gainCycleItem;
    private readonly ToolStripMenuItem _filterMenuItem;
    private readonly ToolStripMenuItem _filterOsItem;
    private readonly ToolStripMenuItem _filterNosItem;
    private readonly ToolStripMenuItem _filterToggleItem;
    private readonly ToolStripMenuItem _muteItem;
    private readonly ToolStripMenuItem _lcdNormalItem;
    private readonly ToolStripMenuItem _lcdTextItem;
    private readonly ToolStripMenuItem _lcdSegmentsItem;
    private readonly ToolStripMenuItem _rgbOffItem;
    private readonly ToolStripMenuItem _rgbOnItem;
    private readonly ToolStripMenuItem _rgbCustomItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _rebootItem;
    private readonly ToolStripMenuItem _exitItem;

    private TrayConfig _config;
    private SettingsForm? _settingsForm;
    private bool _dacConnected;
    private string? _lastAudioEndpointId;
    private bool _pendingConnectSync;
    private int _connectSyncRetriesLeft;
    private DateTime _syncBlockUntilUtc = DateTime.MinValue;
    private bool _audioMuted;
    private GainMode _previewGain = GainMode.Mid;
    private OutputMode _previewOutput;
    private readonly Icon _trayIcon;
    private readonly Icon _trayIconMuted;

    public TrayApplicationContext()
    {
        _config = TrayConfig.Load();
        _config.CustomLcd.EnsureSticksPresetsInitialized(_config.Language);
        _previewOutput = _config.PreferredOutput == OutputMode.PO ? OutputMode.PO : OutputMode.PRE;
        _uiSync = SynchronizationContext.Current;
        _dac = new DacCommandClient(_audio);
        _hotkeys = new GlobalHotkeyManager();
        _hotkeys.HotkeyPressed += OnHotkeyPressed;
        _trayIcon = Theme.CreateTrayIcon(muted: false);
        _trayIconMuted = Theme.CreateTrayIcon(muted: true);

        _statusItem = new ToolStripMenuItem { Enabled = false };
        _outputToggleItem = new ToolStripMenuItem("", null, (_, _) => SendOutputAction(OutputAction.Toggle, "Toggle PO <-> PRE"));
        _outputPreItem = new ToolStripMenuItem("", null, (_, _) => SendOutputAction(OutputAction.PRE, "Switch to PRE"));
        _outputPoItem = new ToolStripMenuItem("", null, (_, _) => SendOutputAction(OutputAction.PO, "Switch to PO"));

        _gainLowItem = new ToolStripMenuItem("", null, (_, _) => SendGainAction(GainAction.Low, "Gain Low"));
        _gainMidItem = new ToolStripMenuItem("", null, (_, _) => SendGainAction(GainAction.Mid, "Gain Mid"));
        _gainHighItem = new ToolStripMenuItem("", null, (_, _) => SendGainAction(GainAction.High, "Gain High"));
        _gainCycleItem = new ToolStripMenuItem("", null, (_, _) => SendGainAction(GainAction.Cycle, "Cycle Gain"));
        _gainMenuItem = new ToolStripMenuItem();
        _gainMenuItem.DropDown.BackColor = Theme.BgSidebar;
        _gainMenuItem.DropDown.ForeColor = Theme.TextPrimary;
        _gainMenuItem.DropDownItems.AddRange([_gainLowItem, _gainMidItem, _gainHighItem, new ToolStripSeparator(), _gainCycleItem]);

        _filterOsItem = new ToolStripMenuItem("", null, (_, _) => SendFilterAction(FilterAction.Os, "Filter OS"));
        _filterNosItem = new ToolStripMenuItem("", null, (_, _) => SendFilterAction(FilterAction.Nos, "Filter NOS"));
        _filterToggleItem = new ToolStripMenuItem("", null, (_, _) => SendFilterAction(FilterAction.Toggle, "Toggle OS / NOS"));
        _filterMenuItem = new ToolStripMenuItem();
        _filterMenuItem.DropDown.BackColor = Theme.BgSidebar;
        _filterMenuItem.DropDown.ForeColor = Theme.TextPrimary;
        _filterMenuItem.DropDownItems.AddRange([_filterOsItem, _filterNosItem, new ToolStripSeparator(), _filterToggleItem]);

        _muteItem = new ToolStripMenuItem("", null, (_, _) => ToggleAudioMute());

        _lcdNormalItem = new ToolStripMenuItem("", null, (_, _) => SetLcdMode(CustomLcdMode.Normal));
        _lcdTextItem = new ToolStripMenuItem("", null, (_, _) => SetLcdMode(CustomLcdMode.Text));
        _lcdSegmentsItem = new ToolStripMenuItem("", null, (_, _) => SetLcdMode(CustomLcdMode.Segments));

        _rgbOffItem = new ToolStripMenuItem("", null, (_, _) => SetRgbState(RgbStateMode.Off));
        _rgbOnItem = new ToolStripMenuItem("", null, (_, _) => SetRgbState(RgbStateMode.On));
        _rgbCustomItem = new ToolStripMenuItem("", null, (_, _) => SetRgbState(RgbStateMode.Custom));

        _settingsItem = new ToolStripMenuItem("", null, (_, _) => OpenSettings());
        _rebootItem = new ToolStripMenuItem("", null, (_, _) => RebootDac());
        _exitItem = new ToolStripMenuItem("", null, (_, _) => ExitThread());

        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = true,
            ShowCheckMargin = true,
            BackColor = Theme.BgSidebar,
            ForeColor = Theme.TextPrimary
        };
        menu.Items.AddRange([
            _statusItem,
            new ToolStripSeparator(),
            _outputToggleItem,
            _outputPreItem,
            _outputPoItem,
            new ToolStripSeparator(),
            _gainMenuItem,
            _filterMenuItem,
            _muteItem,
            new ToolStripSeparator(),
            _lcdNormalItem,
            _lcdTextItem,
            _lcdSegmentsItem,
            new ToolStripSeparator(),
            _rgbOnItem,
            _rgbOffItem,
            _rgbCustomItem,
            new ToolStripSeparator(),
            _settingsItem,
            _rebootItem,
            new ToolStripSeparator(),
            _exitItem
        ]);

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenSettings();

        ApplyHotkeys();
        RefreshDeviceState();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _pollTimer.Tick += (_, _) => RefreshDeviceState();
        _pollTimer.Start();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
            return;

        PostUi(() =>
        {
            _syncBlockUntilUtc = DateTime.UtcNow.AddMilliseconds(1500);
            if (ComputeConnectSyncScope(_config) != SyncScope.None)
            {
                _pendingConnectSync = true;
                _connectSyncRetriesLeft = 3;
            }
        });
    }

    private void ApplyHotkeys()
    {
        var errors = _hotkeys.ApplyBindings(_config.Hotkeys);
        foreach (string err in errors)
            System.Diagnostics.Debug.WriteLine($"[K11Control] Hotkey error: {err}");
    }

    private void RefreshDeviceState()
    {
        var endpoint = _audio.TryGetEndpointInfo();
        bool hidPresent = _hid.EnumerateCollections().Count > 0;
        bool connectedNow = hidPresent || endpoint is not null;
        bool mutedNow = endpoint?.Muted ?? false;
        bool reconnected = connectedNow && !_dacConnected;
        bool endpointBecameReady = endpoint is not null
            && !string.Equals(_lastAudioEndpointId, endpoint.DeviceId, StringComparison.OrdinalIgnoreCase);

        _dacConnected = connectedNow;
        _lastAudioEndpointId = endpoint?.DeviceId;

        if (_audioMuted != mutedNow)
        {
            _audioMuted = mutedNow;
            _notifyIcon.Icon = _audioMuted ? _trayIconMuted : _trayIcon;
        }

        if (reconnected || endpointBecameReady)
        {
            if (ComputeConnectSyncScope(_config) != SyncScope.None)
            {
                _pendingConnectSync = true;
                _connectSyncRetriesLeft = 3;
            }
        }
        else if (!connectedNow)
        {
            _pendingConnectSync = false;
            _connectSyncRetriesLeft = 0;
        }

        if (_pendingConnectSync && endpoint is not null && DateTime.UtcNow >= _syncBlockUntilUtc)
        {
            var startupScope = ComputeConnectSyncScope(_config);
            _pendingConnectSync = false;
            if (startupScope != SyncScope.None)
            {
                SyncRamStateToDac("USB Connect", startupScope, ok =>
                {
                    if (!ok && _connectSyncRetriesLeft > 0)
                    {
                        _connectSyncRetriesLeft--;
                        _pendingConnectSync = true;
                    }
                });
            }
        }

        UpdateUiIndicators();
    }

    private void UpdateUiIndicators()
    {
        AppLanguage lang = _config.Language;

        _statusItem.Text = AppStrings.TrayStatus(lang, _dacConnected);

        _outputToggleItem.Text = AppStrings.OutToggleBtn(lang);
        _outputPreItem.Text = AppStrings.OutPreBtn(lang);
        _outputPoItem.Text = AppStrings.OutPoBtn(lang);

        _outputToggleItem.ShortcutKeyDisplayString = FormatShortcut(_config.Hotkeys.ToggleOutput);
        _outputPreItem.ShortcutKeyDisplayString = FormatShortcut(_config.Hotkeys.SwitchToPre);
        _outputPoItem.ShortcutKeyDisplayString = FormatShortcut(_config.Hotkeys.SwitchToPo);

        _gainMenuItem.Text = AppStrings.TrayGainMenu(lang);
        _gainLowItem.Text = AppStrings.GainLowBtn(lang);
        _gainMidItem.Text = AppStrings.GainMidBtn(lang);
        _gainHighItem.Text = AppStrings.GainHighBtn(lang);
        _gainCycleItem.Text = AppStrings.TrayGainCycle(lang);
        _gainCycleItem.ShortcutKeyDisplayString = FormatShortcut(_config.Hotkeys.CycleGain);

        _filterMenuItem.Text = AppStrings.TrayFilterMenu(lang);
        _filterOsItem.Text = AppStrings.FilterOsBtn(lang);
        _filterNosItem.Text = AppStrings.FilterNosBtn(lang);
        _filterToggleItem.Text = AppStrings.TrayFilterToggle(lang);
        _filterToggleItem.ShortcutKeyDisplayString = FormatShortcut(_config.Hotkeys.ToggleFilter);

        _muteItem.Text = AppStrings.TrayMute(lang, _audioMuted);
        _muteItem.Checked = _audioMuted;
        _muteItem.Enabled = _dacConnected;
        _muteItem.ShortcutKeyDisplayString = FormatShortcut(_config.Hotkeys.ToggleMute);

        _lcdNormalItem.Text = AppStrings.TrayLcdNormal(lang);
        _lcdTextItem.Text = AppStrings.TrayLcdText(lang);
        _lcdSegmentsItem.Text = AppStrings.TrayLcdSegments(lang);
        _lcdNormalItem.Checked = _config.CustomLcd.Mode == CustomLcdMode.Normal;
        _lcdTextItem.Checked = _config.CustomLcd.Mode == CustomLcdMode.Text;
        _lcdSegmentsItem.Checked = _config.CustomLcd.Mode == CustomLcdMode.Segments;
        string? lcdShortcut = FormatShortcut(_config.Hotkeys.ToggleCustomText);
        bool useSegShortcut = _config.CustomLcd.Mode == CustomLcdMode.Segments
            || (_config.CustomLcd.Mode == CustomLcdMode.Normal && _config.CustomLcd.LastCustomMode == CustomLcdMode.Segments);
        _lcdTextItem.ShortcutKeyDisplayString = useSegShortcut ? null : lcdShortcut;
        _lcdSegmentsItem.ShortcutKeyDisplayString = useSegShortcut ? lcdShortcut : null;

        _rgbOffItem.Text = AppStrings.TrayRgbOff(lang);
        _rgbOnItem.Text = AppStrings.TrayRgbOn(lang);
        _rgbCustomItem.Text = AppStrings.TrayRgbCustom(lang);
        _rgbOffItem.Checked = _config.Rgb.State == RgbStateMode.Off;
        _rgbOnItem.Checked = _config.Rgb.State == RgbStateMode.On;
        _rgbCustomItem.Checked = _config.Rgb.State == RgbStateMode.Custom;
        _rgbCustomItem.ShortcutKeyDisplayString = FormatShortcut(_config.Hotkeys.ToggleRgb);

        _settingsItem.Text = AppStrings.TrayOpenApp(lang);
        _rebootItem.Text = AppStrings.RebootBtn(lang);
        _exitItem.Text = AppStrings.TrayExit(lang);

        _notifyIcon.Text = AppStrings.TrayTooltip(lang, _dacConnected, _audioMuted);

        if (_settingsForm is { IsDisposed: false })
            _settingsForm.SetDeviceStatus(_dacConnected, _audioMuted);
    }

    private static string? FormatShortcut(HotkeyBinding binding) =>
        binding.IsConfigured ? binding.ToString() : null;

    private void OnHotkeyPressed(HotkeyActionId action)
    {
        switch (action)
        {
            case HotkeyActionId.ToggleOutput:
                SendOutputAction(OutputAction.Toggle, "Hotkey Toggle PO <-> PRE");
                break;
            case HotkeyActionId.SwitchToPre:
                SendOutputAction(OutputAction.PRE, "Hotkey Switch to PRE");
                break;
            case HotkeyActionId.SwitchToPo:
                SendOutputAction(OutputAction.PO, "Hotkey Switch to PO");
                break;
            case HotkeyActionId.ToggleCustomText:
                SetLcdMode(_config.CustomLcd.Enabled ? CustomLcdMode.Normal : _config.CustomLcd.LastCustomMode);
                break;
            case HotkeyActionId.ToggleRgb:
                CycleRgbState();
                break;
            case HotkeyActionId.ToggleMute:
                ToggleAudioMute();
                break;
            case HotkeyActionId.CycleGain:
                SendGainAction(GainAction.Cycle, "Hotkey Cycle Gain");
                break;
            case HotkeyActionId.ToggleFilter:
                SendFilterAction(FilterAction.Toggle, "Hotkey Toggle OS / NOS");
                break;
            case HotkeyActionId.RebootDac:
                RebootDac();
                break;
        }
    }

    private void ToggleAudioMute()
    {
        if (!_audio.TryToggleMute(out bool nowMuted))
            return;

        _audioMuted = nowMuted;
        _notifyIcon.Icon = _audioMuted ? _trayIconMuted : _trayIcon;
        UpdateUiIndicators();
    }

    private void SendOutputAction(OutputAction action, string reason)
    {
        if (!_dacConnected)
            return;

        _previewOutput = action switch
        {
            OutputAction.PO => OutputMode.PO,
            OutputAction.PRE => OutputMode.PRE,
            _ => _previewOutput == OutputMode.PO ? OutputMode.PRE : OutputMode.PO
        };
        if (_settingsForm is { IsDisposed: false })
            _settingsForm.UpdateLcdPreviewIcons(_previewOutput, _previewGain);

        SendDacDirectAction(reason, cb => _dac.EnqueueOutputAction(action, reason, cb));
    }

    private void SendGainAction(GainAction action, string reason)
    {
        if (!_dacConnected)
            return;

        _previewGain = action switch
        {
            GainAction.Low => GainMode.Low,
            GainAction.Mid => GainMode.Mid,
            GainAction.High => GainMode.High,
            _ => (GainMode)(((int)_previewGain + 1) % 3)
        };
        if (_settingsForm is { IsDisposed: false })
            _settingsForm.UpdateLcdPreviewIcons(_previewOutput, _previewGain);

        SendDacDirectAction(reason, cb => _dac.EnqueueGainAction(action, reason, cb));
    }

    private void SendFilterAction(FilterAction action, string reason) =>
        SendDacDirectAction(reason, cb => _dac.EnqueueFilterAction(action, reason, cb));

    private void SendDacDirectAction(string reason, Action<Action<bool, string?>> enqueue)
    {
        if (!_dacConnected)
            return;

        // Send explicit command or hardware Toggle (0xFF) directly without assuming stale PC state.
        enqueue((ok, err) =>
        {
            if (!ok && !string.IsNullOrWhiteSpace(err))
                System.Diagnostics.Debug.WriteLine($"[K11Control] {reason}: {err}");
        });
    }

    private void SetLcdMode(CustomLcdMode mode)
    {
        _config.CustomLcd.Mode = mode;
        _config.Save();
        SyncRamStateToDac($"LCD {mode}", SyncScope.CustomLcd);
        if (_settingsForm is { IsDisposed: false })
            _settingsForm.UpdateExternalLcdState(_dacConnected, _config.CustomLcd.Mode);
        UpdateUiIndicators();
    }

    private void SetRgbState(RgbStateMode state)
    {
        _config.Rgb.State = state;
        _config.Save();
        SyncScope scope = _config.CustomLcd.RequiresRamSync
            ? (SyncScope.Rgb | SyncScope.CustomLcd)
            : SyncScope.Rgb;
        SyncRamStateToDac($"RGB {state}", scope);
        if (_settingsForm is { IsDisposed: false })
            _settingsForm.UpdateExternalRgbState(_dacConnected, _config.Rgb.State);
        UpdateUiIndicators();
    }

    private void CycleRgbState()
    {
        RgbStateMode next = _config.Rgb.State switch
        {
            RgbStateMode.On => RgbStateMode.Off,
            RgbStateMode.Off => RgbStateMode.Custom,
            _ => RgbStateMode.On
        };
        SetRgbState(next);
    }

    private void RebootDac()
    {
        if (!_dacConnected)
            return;

        _dac.EnqueueReboot((ok, err) =>
        {
            if (!ok && !string.IsNullOrWhiteSpace(err))
                System.Diagnostics.Debug.WriteLine($"[K11Control] Reboot: {err}");

            if (ok)
            {
                PostUi(() =>
                {
                    // Re-sync active RAM state once MCU reboots, even if USB bridge did not drop long enough to trip poll timer.
                    _syncBlockUntilUtc = DateTime.UtcNow.AddMilliseconds(1800);
                    if (ComputeConnectSyncScope(_config) != SyncScope.None)
                    {
                        _pendingConnectSync = true;
                        _connectSyncRetriesLeft = 3;
                    }
                });
            }
        });
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
                _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(
            _config,
            _dacConnected,
            _audioMuted,
            updated =>
            {
                TrayConfig prev = _config;
                _config = updated;
                _config.Save();
                ApplyHotkeys();

                SyncScope scope = ComputeExplicitSettingsApplyScope(prev, updated);
                if (scope != SyncScope.None)
                    SyncRamStateToDac("Settings Applied", scope);
                UpdateUiIndicators();
            },
            lang =>
            {
                _config.Language = lang;
                _config.Save();
                UpdateUiIndicators();
            },
            RebootDac,
            action => SendOutputAction(action, $"UI Output {action}"),
            ToggleAudioMute,
            action => SendGainAction(action, $"UI Gain {action}"),
            action => SendFilterAction(action, $"UI Filter {action}"),
            _previewGain,
            _previewOutput,
            (presets, activeIdx) =>
            {
                _config.CustomLcd.SticksPresets = presets.Select(p => p.Clone()).ToList();
                _config.CustomLcd.ActiveSticksPresetIndex = Math.Clamp(activeIdx, 0, Math.Max(0, _config.CustomLcd.SticksPresets.Count - 1));
                _config.Save();
            });
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    public static SyncScope ComputeConnectSyncScope(TrayConfig cfg)
    {
        // Preserve hardware output mode on startup/reconnect unless EnforceOutputOnConnect is opted in.
        var scope = SyncScope.None;
        if (cfg.EnforceOutputOnConnect)
            scope |= SyncScope.OutputMode;
        if (cfg.CustomLcd.RequiresRamSync)
            scope |= SyncScope.CustomLcd;
        if (cfg.Rgb.State != RgbStateMode.On)
            scope |= SyncScope.Rgb;
        return scope;
    }

    public static SyncScope ComputeSettingsApplyScope(TrayConfig prev, TrayConfig next)
    {
        // Applying settings never alters current live DAC output (explicit Toggle/PO/PRE actions handle that).
        var scope = SyncScope.None;

        bool lcdChanged = prev.CustomLcd.Mode != next.CustomLcd.Mode
            || prev.CustomLcd.EffectiveHideTopIcons != next.CustomLcd.EffectiveHideTopIcons
            || prev.CustomLcd.ScreenOff != next.CustomLcd.ScreenOff
            || !string.Equals(prev.CustomLcd.Text, next.CustomLcd.Text, StringComparison.Ordinal)
            || !prev.CustomLcd.SegmentMasks.SequenceEqual(next.CustomLcd.SegmentMasks)
            || prev.CustomLcd.FrameCount != next.CustomLcd.FrameCount
            || prev.CustomLcd.StepIntervalMs != next.CustomLcd.StepIntervalMs
            || !AreAnimationFramesEqual(prev.CustomLcd, next.CustomLcd)
            || prev.CustomLcd.ActivityRevertDelayMs != next.CustomLcd.ActivityRevertDelayMs;

        if (lcdChanged)
            scope |= SyncScope.CustomLcd;

        bool rgbChanged = prev.Rgb.State != next.Rgb.State
            || prev.Rgb.Effect != next.Rgb.Effect
            || prev.Rgb.Brightness != next.Rgb.Brightness
            || !prev.Rgb.ZoneHexColors.SequenceEqual(next.Rgb.ZoneHexColors, StringComparer.OrdinalIgnoreCase);

        if (rgbChanged)
            scope |= SyncScope.Rgb;

        // Re-assert custom LCD state (text/masks/blank/top-icon override) whenever pushing other RAM updates.
        if (scope != SyncScope.None && next.CustomLcd.RequiresRamSync)
            scope |= SyncScope.CustomLcd;

        return scope;
    }

    public static SyncScope ComputeExplicitSettingsApplyScope(TrayConfig prev, TrayConfig next)
    {
        // Explicit Apply / Save re-pushes active custom RGB and LCD into DAC RAM even if config fields stayed identical.
        var scope = ComputeSettingsApplyScope(prev, next);
        if (next.Rgb.State != RgbStateMode.On)
            scope |= SyncScope.Rgb;
        if (next.CustomLcd.RequiresRamSync)
            scope |= SyncScope.CustomLcd;
        return scope;
    }

    private static bool AreAnimationFramesEqual(CustomLcdConfig a, CustomLcdConfig b)
    {
        int count = Math.Clamp(Math.Max(a.FrameCount, b.FrameCount), 1, SticksPreset.MaxFrames);
        for (int f = 0; f < count; f++)
        {
            if (!a.ResolveFrameMasks(f).SequenceEqual(b.ResolveFrameMasks(f)))
                return false;
        }
        return true;
    }

    private void SyncRamStateToDac(string reason, SyncScope scope = SyncScope.All, Action<bool>? onCompleted = null)
    {
        // All state stays in PC JSON + DAC RAM (zero MCU Flash writes).
        if (!_dacConnected)
        {
            onCompleted?.Invoke(false);
            return;
        }

        _dac.EnqueueSync(_config, scope, reason, (ok, err) =>
        {
            if (!ok && !string.IsNullOrWhiteSpace(err))
                System.Diagnostics.Debug.WriteLine($"[K11Control] {reason}: {err}");
            if (onCompleted is not null)
                PostUi(() => onCompleted(ok));
        });
    }

    private void PostUi(Action action)
    {
        if (_uiSync is not null)
            _uiSync.Post(_ => action(), null);
        else
            action();
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _pollTimer.Stop();
        _pollTimer.Dispose();
        _settingsForm?.Close();
        _hotkeys.Dispose();
        _dac.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayIcon.Dispose();
        _trayIconMuted.Dispose();
        base.ExitThreadCore();
    }
}
