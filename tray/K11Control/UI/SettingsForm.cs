using System.Runtime.Versioning;
using K11Control.Audio;
using K11Control.Config;
using K11Control.Protocol;
using K11Control.Stage0;
using K11Control.Usb;

namespace K11Control.UI;

[SupportedOSPlatform("windows")]
public sealed class SettingsForm : Form
{
    private enum ApplyFeedbackState
    {
        Normal,
        Success,
        Offline,
        Error
    }

    private readonly TrayConfig _config;
    private readonly Action<TrayConfig, Action<bool>> _onApplyAsync;
    private readonly Func<K11AudioEndpointInfo?> _onFixBalance;
    private readonly Action<AppLanguage> _onLanguageChanged;
    private readonly Action _onRebootDac;
    private readonly Action<OutputAction> _onOutputAction;
    private readonly Action<GainAction> _onGainAction;
    private readonly Action<FilterAction> _onFilterAction;
    private readonly Action _onToggleMute;
    private readonly Action<List<SticksPreset>, int> _onPresetsPersisted;

    private readonly List<SidebarNavButton> _navButtons = [];
    private readonly Panel[] _pages = new Panel[5];
    private int _activePage;

    private SegmentedToggle<AppLanguage> _langToggle = null!;
    private Label _pageTitleLabel = null!;
    private SidebarMuteButton _muteBtn = null!;
    private ModernButton _cancelBtn = null!;
    private ModernButton _applyBtn = null!;
    private ModernButton _saveCloseBtn = null!;
    private bool _dacConnected;
    private bool _audioMuted;
    private K11AudioEndpointInfo? _audioEndpoint;
    private bool _balanceBannerVisible;
    private readonly System.Windows.Forms.Timer _applyFeedbackTimer = new();
    private ApplyFeedbackState _applyFeedbackState = ApplyFeedbackState.Normal;
    private int _applyFeedbackSeq;

    // Output & Power page
    private Label _outTitle = null!;
    private ModernButton _btnToggleOut = null!;
    private ModernButton _btnPreOut = null!;
    private ModernButton _btnPoOut = null!;
    private Label _dacModesTitle = null!;
    private Label _gainLbl = null!;
    private ModernButton _btnGainLow = null!;
    private ModernButton _btnGainMid = null!;
    private ModernButton _btnGainHigh = null!;
    private Label _filterLbl = null!;
    private ModernButton _btnFilterOs = null!;
    private ModernButton _btnFilterNos = null!;
    private Label _bootTitle = null!;
    private ModernToggleSwitch _autostartSwitch = null!;
    private ModernToggleSwitch _enforceOutputSwitch = null!;
    private SegmentedToggle<OutputMode> _startupOutputToggle = null!;
    private ModernButton _rebootBtn = null!;
    private SidebarBalanceButton _balanceCard = null!;
    private readonly ToolTip _toolTip = new();

    // LCD page (Normal / Text / Segments with saved presets & up to 4-frame animation)
    private RoundedCardPanel _lcdModeCard = null!;
    private Label _lcdCardTitle = null!;
    private SegmentedToggle<CustomLcdMode> _lcdModeToggle = null!;
    private LcdPreviewWidget _lcdPreview = null!;
    private ModernButton _btnAnimPlay = null!;
    private FrameCapsuleSelector _frameCapsule = null!;
    private DarkSelectorButton _frameCountCombo = null!;
    private ModernButton _btnClearFrame = null!;
    private RoundedCardPanel _lcdTextCard = null!;
    private Label _lcdSettingsTitle = null!;
    private Label _lcdTextLbl = null!;
    private Panel _lcdTextWrap = null!;
    private TextBox _lcdTextBox = null!;
    private Label _presetLbl = null!;
    private DarkSelectorButton _presetCombo = null!;
    private ModernButton _btnSavePreset = null!;
    private ModernButton _btnPresetMore = null!;
    private ContextMenuStrip? _presetMoreMenu;
    private Label _row2Lbl = null!;
    private Panel _templateWordWrap = null!;
    private TextBox _templateWordBox = null!;
    private ModernDelayStepper _animSpeedStepper = null!;
    private Label _lcdDelayLbl = null!;
    private ModernDelayStepper _lcdDelayStepper = null!;
    private ModernToggleSwitch _lcdScreenOffSwitch = null!;
    private ModernToggleSwitch _lcdHideIconsSwitch = null!;
    private readonly bool[] _hideTopIconsByMode = new bool[3];

    private readonly List<SticksPreset> _editingPresets = [];
    private int _activePresetIdx;
    private int _activeFrameIdx;
    private int _animPlaybackIdx;
    private bool _animPlaying;
    private bool _savedBadgeActive;
    private readonly System.Windows.Forms.Timer _animTimer = new();
    private readonly System.Windows.Forms.Timer _saveFeedbackTimer = new();

    // RGB page
    private RgbStudioControl _rgbStudio = null!;

    // Hotkeys page
    private Label _hkTitle = null!;
    private Label _hkSub = null!;
    private readonly List<Label> _hkRowLabels = [];
    private HotkeyCaptureBox _hkToggleOut = null!;
    private HotkeyCaptureBox _hkPre = null!;
    private HotkeyCaptureBox _hkPo = null!;
    private HotkeyCaptureBox _hkLcd = null!;
    private HotkeyCaptureBox _hkRgb = null!;
    private HotkeyCaptureBox _hkMute = null!;
    private HotkeyCaptureBox _hkGainCycle = null!;
    private HotkeyCaptureBox _hkFilterToggle = null!;
    private HotkeyCaptureBox _hkReboot = null!;
    private ModernButton _hkResetBtn = null!;

    // Diagnostics page
    private ModernButton _diagRefreshBtn = null!;
    private ModernButton _diagCh0Btn = null!;
    private ModernButton _diagCh1Btn = null!;
    private ModernButton _diagMuteBtn = null!;
    private ModernButton _diagCol01Btn = null!;
    private ModernButton _diagCol02Btn = null!;
    private ModernButton _diagFlasherBtn = null!;
    private TextBox _diagOutput = null!;

    public SettingsForm(
        TrayConfig currentConfig,
        bool dacConnected,
        bool audioMuted,
        Action<TrayConfig> onApply,
        Action<AppLanguage> onLanguageChanged,
        Action onRebootDac,
        Action<OutputAction> onOutputAction,
        Action onToggleMute,
        Action<GainAction>? onGainAction = null,
        Action<FilterAction>? onFilterAction = null,
        GainMode initialPreviewGain = GainMode.Mid,
        OutputMode? initialPreviewOutput = null,
        Action<List<SticksPreset>, int>? onPresetsPersisted = null,
        Action<TrayConfig, Action<bool>>? onApplyWithFeedback = null,
        Func<K11AudioEndpointInfo?>? onFixBalance = null,
        K11AudioEndpointInfo? initialAudioEndpoint = null)
    {
        _config = CloneConfigSnapshot(currentConfig);
        _dacConnected = dacConnected;
        _audioMuted = audioMuted;
        _audioEndpoint = dacConnected ? initialAudioEndpoint : null;
        _onApplyAsync = onApplyWithFeedback ?? ((cfg, cb) => { onApply(cfg); cb(true); });
        _onFixBalance = onFixBalance ?? (() => null);
        _onLanguageChanged = onLanguageChanged;
        _onRebootDac = onRebootDac;
        _onOutputAction = onOutputAction;
        _onGainAction = onGainAction ?? (_ => { });
        _onFilterAction = onFilterAction ?? (_ => { });
        _onToggleMute = onToggleMute;
        _onPresetsPersisted = onPresetsPersisted ?? ((_, _) => { });

        _animTimer.Tick += (_, _) => OnAnimPreviewTick();
        _saveFeedbackTimer.Interval = 1100;
        _saveFeedbackTimer.Tick += (_, _) =>
        {
            _saveFeedbackTimer.Stop();
            _savedBadgeActive = false;
            _btnSavePreset.Text = AppStrings.LcdSavePresetBtn(_config.Language);
        };
        _applyFeedbackTimer.Interval = 1400;
        _applyFeedbackTimer.Tick += (_, _) => ClearApplyFeedback();

        Text = "FiiO K11 R2R Control";
        Icon = Theme.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.BgWindow;
        ForeColor = Theme.TextPrimary;
        Font = Theme.FontBody;
        ClientSize = new Size(748, 580);

        BuildUi();
        WireBlurTextInputs(this);
        LoadValues(initialPreviewGain, initialPreviewOutput);
        ApplyLanguage(_config.Language);
        SelectPage(0);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkWindowChrome(Handle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer.Stop();
            _animTimer.Dispose();
            _saveFeedbackTimer.Stop();
            _saveFeedbackTimer.Dispose();
            _applyFeedbackTimer.Stop();
            _applyFeedbackTimer.Dispose();
            _presetMoreMenu?.Dispose();
            _presetMoreMenu = null;
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    public void UpdateExternalLcdState(bool connected, CustomLcdMode lcdMode)
    {
        SetDeviceStatus(connected, _audioMuted);

        _config.CustomLcd.Mode = lcdMode;
        _lcdModeToggle.SetSelectedSilently(lcdMode);
        _lcdPreview.PreviewMode = lcdMode;
        UpdateLcdFieldStates();
        SyncAnimPreviewPlayback();
    }

    public void UpdateExternalRgbState(bool connected, RgbStateMode rgbState)
    {
        SetDeviceStatus(connected, _audioMuted);

        // Update only RGB mode without discarding pending edits on the LCD tab.
        _config.Rgb.State = rgbState;
        _rgbStudio.ApplyExternalStateMode(rgbState);
    }

    public void UpdateLcdPreviewIcons(OutputMode output, GainMode gain)
    {
        _lcdPreview.PreviewOutput = output;
        _lcdPreview.PreviewGain = gain;
    }

    public void SetDeviceStatus(bool connected, bool muted)
    {
        SetDeviceStatus(connected, muted, connected ? _audioEndpoint : null);
    }

    public void SetDeviceStatus(bool connected, bool muted, K11AudioEndpointInfo? audioEndpoint)
    {
        _dacConnected = connected;
        _audioMuted = muted;
        _audioEndpoint = connected ? audioEndpoint : null;
        _muteBtn.UpdateState(connected, muted, _config.Language);
        _rebootBtn.Enabled = connected;
        UpdateChannelBalanceBanner();
    }

    private void UpdateChannelBalanceBanner()
    {
        if (_balanceCard is null)
            return;

        bool show = _dacConnected
            && _audioEndpoint is not null
            && CoreAudioVolumeService.IsChannelImbalanced(_audioEndpoint.Ch0Db, _audioEndpoint.Ch1Db);

        _balanceBannerVisible = show;
        if (_balanceCard.Visible != show)
            _balanceCard.Visible = show;
        if (show && _audioEndpoint is not null)
        {
            float targetDb = CoreAudioVolumeService.ComputeTargetBalancedDb(
                _audioEndpoint.Ch0Db, _audioEndpoint.Ch1Db, _audioEndpoint.MinDb, _audioEndpoint.MaxDb);
            string warnText = AppStrings.OutBalanceWarn(_config.Language, _audioEndpoint.Ch0Db, _audioEndpoint.Ch1Db);
            string targetText = AppStrings.SidebarBalanceTarget(_config.Language, targetDb);
            _balanceCard.UpdateLabels(warnText, targetText);
            if (!_balanceCard.Enabled)
                _balanceCard.Enabled = true;
        }
    }

    private void BuildUi()
    {
        // Left Sidebar
        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 186,
            BackColor = Theme.BgSidebar,
            Padding = new Padding(12, 10, 12, 12)
        };
        sidebar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.BorderSubtle);
            e.Graphics.DrawLine(pen, 0, 0, sidebar.Width - 1, 0);
            e.Graphics.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
        };

        var brandTitle = new Label
        {
            Text = "FIIO K11 R2R",
            Font = Theme.FontBrand,
            ForeColor = Theme.TextPrimary,
            Location = new Point(16, 10),
            AutoSize = true
        };
        var brandSub = new Label
        {
            Text = "CONTROL STUDIO",
            Font = new Font("Segoe UI Semibold", 7.6f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            Location = new Point(17, 30),
            AutoSize = true
        };

        sidebar.Controls.AddRange([brandTitle, brandSub]);

        NavIconKind[] icons =
        [
            NavIconKind.Output,
            NavIconKind.Lcd,
            NavIconKind.Rgb,
            NavIconKind.Hotkeys,
            NavIconKind.Diagnostics
        ];

        for (int i = 0; i < icons.Length; i++)
        {
            int idx = i;
            var btn = new SidebarNavButton
            {
                IconKind = icons[i],
                Location = new Point(11, 60 + i * 44),
                Size = new Size(164, 38)
            };
            btn.Click += (_, _) => SelectPage(idx);
            _navButtons.Add(btn);
            sidebar.Controls.Add(btn);
        }

        // Bottom sidebar stack: clickable actions only (balance fix, mute toggle, reboot DAC)
        _balanceCard = new SidebarBalanceButton
        {
            Location = new Point(11, 364),
            Size = new Size(164, 50),
            Visible = false
        };
        _balanceCard.Click += (_, _) =>
        {
            _balanceCard.Enabled = false;
            var updated = _onFixBalance();
            if (updated is not null)
                _audioEndpoint = updated;
            UpdateChannelBalanceBanner();
        };

        _muteBtn = new SidebarMuteButton
        {
            Location = new Point(11, 420),
            Size = new Size(164, 32)
        };
        _muteBtn.Click += (_, _) => _onToggleMute();

        _rebootBtn = new ModernButton
        {
            Variant = ButtonVariant.Danger,
            ShowResetIcon = true,
            CornerRadius = 8f,
            Font = Theme.FontSmall,
            Location = new Point(11, 458),
            Size = new Size(164, 32)
        };
        _rebootBtn.Click += (_, _) => _onRebootDac();

        var langLabel = new Label
        {
            Text = "LANGUAGE / ЯЗЫК",
            Font = new Font("Segoe UI Semibold", 7.3f, FontStyle.Bold),
            ForeColor = Theme.TextMuted,
            Location = new Point(16, 508),
            AutoSize = true
        };
        _langToggle = new SegmentedToggle<AppLanguage>
        {
            Location = new Point(11, 528),
            Size = new Size(164, 34)
        };
        _langToggle.SetItems((AppLanguage.Ru, "RU"), (AppLanguage.En, "EN"));
        _langToggle.SelectedChanged += (_, _) =>
        {
            AppLanguage lang = _langToggle.SelectedValue;
            if (_config.Language == lang)
                return;
            _config.Language = lang;
            ApplyLanguage(lang);
            _onLanguageChanged(lang);
        };
        sidebar.Controls.AddRange([_balanceCard, _muteBtn, _rebootBtn, langLabel, _langToggle]);

        // Right Workspace + Footer
        var rightShell = new Panel { Dock = DockStyle.Fill, BackColor = Theme.BgWindow };

        var headerPanel = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Theme.BgWindow };
        headerPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.BorderSubtle);
            e.Graphics.DrawLine(pen, 0, 0, headerPanel.Width - 1, 0);
        };
        _pageTitleLabel = new Label
        {
            Location = new Point(14, 6),
            Size = new Size(512, 28),
            Font = Theme.FontTitle,
            ForeColor = Theme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        };
        headerPanel.Controls.Add(_pageTitleLabel);

        var footerPanel = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Theme.BgWindow };

        _saveCloseBtn = new ModernButton { Width = 166, Height = 34, Location = new Point(380, 6), Variant = ButtonVariant.Primary };
        _applyBtn = new ModernButton { Width = 114, Height = 34, Location = new Point(258, 6), Variant = ButtonVariant.Secondary };
        _cancelBtn = new ModernButton { Width = 92, Height = 34, Location = new Point(18, 6), Variant = ButtonVariant.Secondary };

        _applyBtn.Click += (_, _) => ApplyCurrentEdits(showFeedback: true);
        _saveCloseBtn.Click += (_, _) =>
        {
            ApplyCurrentEdits(showFeedback: false);
            Close();
        };
        _cancelBtn.Click += (_, _) => Close();
        footerPanel.Controls.AddRange([_cancelBtn, _applyBtn, _saveCloseBtn]);

        var pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.BgWindow, Padding = new Padding(16, 4, 16, 4) };

        _pages[0] = BuildOutputPage();
        _pages[1] = BuildLcdPage();
        _pages[2] = BuildRgbPage();
        _pages[3] = BuildHotkeysPage();
        _pages[4] = BuildDiagnosticsPage();

        foreach (var p in _pages)
        {
            p.Dock = DockStyle.Fill;
            p.Visible = false;
            pageHost.Controls.Add(p);
        }

        rightShell.Controls.Add(pageHost);
        rightShell.Controls.Add(headerPanel);
        rightShell.Controls.Add(footerPanel);

        Controls.Add(rightShell);
        Controls.Add(sidebar);
        SetDeviceStatus(_dacConnected, _audioMuted);
    }

    private void WireBlurTextInputs(Control root)
    {
        bool wrapsTextBox = root is not TextBox && root.Controls.OfType<TextBox>().Any();
        if (root is not TextBox && !wrapsTextBox)
        {
            root.MouseDown += (_, _) =>
            {
                if (ActiveControl is TextBox)
                    ActiveControl = null;
            };
        }

        foreach (Control child in root.Controls)
            WireBlurTextInputs(child);
    }

    private void SelectPage(int index)
    {
        if (ActiveControl is TextBox)
            ActiveControl = null;

        if (index != _activePage)
            ClearApplyFeedback();

        _activePage = index;
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = i == index;
            _navButtons[i].Selected = i == index;
        }
        _pageTitleLabel.Text = AppStrings.PageTitle(_config.Language, index);
        SyncAnimPreviewPlayback();
    }

    private Panel BuildOutputPage()
    {
        var page = new Panel { BackColor = Theme.BgWindow };

        // Card 1: Direct / Universal Output Switching (14px top / 16px bottom pad)
        var switchCard = new RoundedCardPanel { Location = new Point(0, 0), Size = new Size(526, 130) };
        _outTitle = new Label { Font = Theme.FontSection, ForeColor = Theme.TextPrimary, Location = new Point(16, 14), AutoSize = true };

        _btnToggleOut = new ModernButton
        {
            Variant = ButtonVariant.Secondary,
            Location = new Point(16, 40),
            Size = new Size(494, 34)
        };
        _btnToggleOut.Click += (_, _) =>
        {
            _lcdPreview.PreviewOutput = _lcdPreview.PreviewOutput == OutputMode.PO ? OutputMode.PRE : OutputMode.PO;
            _onOutputAction(OutputAction.Toggle);
        };

        _btnPreOut = new ModernButton
        {
            Variant = ButtonVariant.Secondary,
            Location = new Point(16, 80),
            Size = new Size(242, 34)
        };
        _btnPreOut.Click += (_, _) =>
        {
            _lcdPreview.PreviewOutput = OutputMode.PRE;
            _onOutputAction(OutputAction.PRE);
        };

        _btnPoOut = new ModernButton
        {
            Variant = ButtonVariant.Secondary,
            Location = new Point(268, 80),
            Size = new Size(242, 34)
        };
        _btnPoOut.Click += (_, _) =>
        {
            _lcdPreview.PreviewOutput = OutputMode.PO;
            _onOutputAction(OutputAction.PO);
        };

        switchCard.Controls.AddRange([_outTitle, _btnToggleOut, _btnPreOut, _btnPoOut]);

        // Card 2: DAC Gain (L/M/H) & Sampling Filter (OS/NOS) (10px below switchCard)
        var modesCard = new RoundedCardPanel { Location = new Point(0, 140), Size = new Size(526, 126) };
        _dacModesTitle = new Label { Font = Theme.FontSection, ForeColor = Theme.TextPrimary, Location = new Point(16, 14), AutoSize = true, UseMnemonic = false };

        _gainLbl = new Label
        {
            Font = Theme.FontBody,
            ForeColor = Theme.TextSecondary,
            Location = new Point(16, 40),
            Size = new Size(130, 32),
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        };
        _btnGainLow = new ModernButton { Variant = ButtonVariant.Secondary, Location = new Point(154, 40), Size = new Size(113, 32) };
        _btnGainMid = new ModernButton { Variant = ButtonVariant.Secondary, Location = new Point(275, 40), Size = new Size(114, 32) };
        _btnGainHigh = new ModernButton { Variant = ButtonVariant.Secondary, Location = new Point(397, 40), Size = new Size(113, 32) };
        _btnGainLow.Click += (_, _) => { _lcdPreview.PreviewGain = GainMode.Low; _onGainAction(GainAction.Low); };
        _btnGainMid.Click += (_, _) => { _lcdPreview.PreviewGain = GainMode.Mid; _onGainAction(GainAction.Mid); };
        _btnGainHigh.Click += (_, _) => { _lcdPreview.PreviewGain = GainMode.High; _onGainAction(GainAction.High); };

        _filterLbl = new Label
        {
            Font = Theme.FontBody,
            ForeColor = Theme.TextSecondary,
            Location = new Point(16, 78),
            Size = new Size(130, 32),
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        };
        _btnFilterOs = new ModernButton { Variant = ButtonVariant.Secondary, Location = new Point(154, 78), Size = new Size(174, 32) };
        _btnFilterNos = new ModernButton { Variant = ButtonVariant.Secondary, Location = new Point(336, 78), Size = new Size(174, 32) };
        _btnFilterOs.Click += (_, _) => _onFilterAction(FilterAction.Os);
        _btnFilterNos.Click += (_, _) => _onFilterAction(FilterAction.Nos);

        modesCard.Controls.AddRange([_dacModesTitle, _gainLbl, _btnGainLow, _btnGainMid, _btnGainHigh, _filterLbl, _btnFilterOs, _btnFilterNos]);

        // Card 3: Windows Autostart & Startup Output selection (10px below modesCard)
        var bootCard = new RoundedCardPanel { Location = new Point(0, 276), Size = new Size(526, 148) };
        _bootTitle = new Label { Font = Theme.FontSection, ForeColor = Theme.TextPrimary, Location = new Point(16, 14), AutoSize = true, UseMnemonic = false };

        _autostartSwitch = new ModernToggleSwitch
        {
            Location = new Point(16, 38),
            Size = new Size(494, 28)
        };
        _autostartSwitch.CheckedChanged += (_, _) =>
        {
            if (!AutostartManager.TrySetEnabled(_autostartSwitch.Checked, out string? err) && !string.IsNullOrWhiteSpace(err))
                _autostartSwitch.SetCheckedSilently(AutostartManager.IsEnabled());
        };

        _enforceOutputSwitch = new ModernToggleSwitch
        {
            Location = new Point(16, 68),
            Size = new Size(494, 28)
        };
        _startupOutputToggle = new SegmentedToggle<OutputMode> { Location = new Point(16, 100), Size = new Size(494, 32) };
        _startupOutputToggle.SetItemsWithAccents(
            (OutputMode.PO, "PO", Theme.Accent),
            (OutputMode.PRE, "LO SE", Theme.AccentAmber));

        _enforceOutputSwitch.CheckedChanged += (_, _) => _startupOutputToggle.Enabled = _enforceOutputSwitch.Checked;
        bootCard.Controls.AddRange([_bootTitle, _autostartSwitch, _enforceOutputSwitch, _startupOutputToggle]);

        page.Controls.AddRange([switchCard, modesCard, bootCard]);
        return page;
    }

    private Panel BuildLcdPage()
    {
        var page = new Panel { BackColor = Theme.BgWindow };

        // Card 1: 3-Mode Switch (Normal / Text / Segments) + VA LCD Preview + Canvas Toolbar (in Segments)
        _lcdModeCard = new RoundedCardPanel { Location = new Point(0, 0), Size = new Size(526, 204) };
        _lcdCardTitle = new Label { Font = Theme.FontSection, ForeColor = Theme.TextPrimary, Location = new Point(16, 14), AutoSize = true };

        _lcdModeToggle = new SegmentedToggle<CustomLcdMode> { Location = new Point(16, 40), Size = new Size(494, 34) };
        _lcdModeToggle.SetItems(
            (CustomLcdMode.Normal, "Normal"),
            (CustomLcdMode.Text, "Text"),
            (CustomLcdMode.Segments, "Segments"));
        _lcdModeToggle.SelectedChanged += (_, _) =>
        {
            _lcdPreview.PreviewMode = _lcdModeToggle.SelectedValue;
            UpdateLcdFieldStates();
            SyncAnimPreviewPlayback();
        };

        _lcdPreview = new LcdPreviewWidget { Location = new Point(16, 84), Size = new Size(494, 106) };
        _lcdPreview.SegmentMasksChanged += (_, _) => OnPreviewSegmentMasksEdited();

        // Canvas animation & drawing toolbar directly under the LCD preview (Segments only), matching upper toggle style
        _btnAnimPlay = new ModernButton { PlayGlyph = PlayGlyphKind.Play, Variant = ButtonVariant.Secondary, CornerRadius = 8f, Location = new Point(16, 200), Size = new Size(36, 34) };
        _btnAnimPlay.Click += (_, _) => ToggleAnimPreviewPlayback();

        _frameCapsule = new FrameCapsuleSelector { Location = new Point(60, 200), Size = new Size(252, 34) };
        _frameCapsule.FrameClicked += (_, idx) => SelectEditedFrame(idx);

        _frameCountCombo = new DarkSelectorButton { Location = new Point(320, 200), Size = new Size(98, 34), CornerRadius = 8f, SubtleBorder = true };
        _frameCountCombo.SelectedIndexChanged += (_, _) => SetActivePresetFrameCount(_frameCountCombo.SelectedIndex + 1);

        _btnClearFrame = new ModernButton { Variant = ButtonVariant.Danger, CornerRadius = 8f, Font = Theme.FontBody, Location = new Point(426, 200), Size = new Size(84, 34) };
        _btnClearFrame.Click += (_, _) => ClearActiveEditedFrame();

        _lcdModeCard.Controls.AddRange([_lcdCardTitle, _lcdModeToggle, _lcdPreview, _btnAnimPlay, _frameCapsule, _frameCountCombo, _btnClearFrame]);

        // Card 2: Custom Screen Settings (174px in Normal/Text at y=214; 214px in Segments at y=258)
        _lcdTextCard = new RoundedCardPanel { Location = new Point(0, 214), Size = new Size(526, 174) };
        _lcdSettingsTitle = new Label { Font = Theme.FontSection, ForeColor = Theme.TextPrimary, Location = new Point(16, 14), AutoSize = true };

        _lcdTextLbl = new Label { Font = Theme.FontBody, ForeColor = Theme.TextSecondary, Location = new Point(16, 44), Size = new Size(190, 34), TextAlign = ContentAlignment.MiddleLeft };

        _lcdTextWrap = new Panel
        {
            Location = new Point(214, 44),
            Size = new Size(296, 34),
            BackColor = Theme.BgInput
        };
        _lcdTextWrap.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.BgCard);
            var r = new RectangleF(0.5f, 0.5f, _lcdTextWrap.Width - 2f, _lcdTextWrap.Height - 2f);
            using var path = Theme.CreateRoundedPath(r, 7f);
            using var fill = new SolidBrush(Theme.BgInput);
            Color rim = _lcdTextBox.Focused ? Theme.Accent : (_lcdTextBox.Enabled ? Theme.BorderStrong : Theme.BorderSubtle);
            using var border = new Pen(rim, 1f);
            g.FillPath(fill, path);
            g.DrawPath(border, path);

            string cnt = $"{_lcdTextBox.Text.Length}/7";
            TextRenderer.DrawText(g, cnt, Theme.FontSmall,
                new Rectangle(_lcdTextWrap.Width - 52, 0, 42, _lcdTextWrap.Height),
                Theme.TextMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        _lcdTextWrap.Click += (_, _) => _lcdTextBox.Focus();

        _lcdTextBox = new TextBox
        {
            Width = 224,
            MaxLength = 7,
            BackColor = Theme.BgInput,
            ForeColor = Theme.Accent,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 13f, FontStyle.Bold),
            CharacterCasing = CharacterCasing.Normal
        };
        _lcdTextBox.Location = new Point(12, Math.Max(4, (_lcdTextWrap.Height - _lcdTextBox.PreferredHeight) / 2));
        _lcdTextBox.GotFocus += (_, _) => _lcdTextWrap.Invalidate();
        _lcdTextBox.LostFocus += (_, _) => _lcdTextWrap.Invalidate();
        _lcdTextBox.TextChanged += (_, _) =>
        {
            _lcdPreview.CustomText = _lcdTextBox.Text;
            _lcdTextWrap.Invalidate();
        };
        _lcdTextWrap.Controls.Add(_lcdTextBox);

        // Segments Row 1: Preset Selector + Save + More Actions (...)
        _presetLbl = new Label { Font = Theme.FontBody, ForeColor = Theme.TextSecondary, Location = new Point(16, 42), Size = new Size(96, 34), TextAlign = ContentAlignment.MiddleLeft };
        _presetCombo = new DarkSelectorButton { Location = new Point(114, 42), Size = new Size(248, 34) };
        _presetCombo.SelectedIndexChanged += (_, _) => SelectPresetIndex(_presetCombo.SelectedIndex);

        _btnSavePreset = new ModernButton { Variant = ButtonVariant.Primary, CornerRadius = 7f, Font = Theme.FontSmall, Location = new Point(368, 42), Size = new Size(100, 34) };
        _btnSavePreset.Click += (_, _) => SaveActivePresetToConfig();

        _btnPresetMore = new ModernButton { ShowDotsIcon = true, Variant = ButtonVariant.Secondary, CornerRadius = 7f, Location = new Point(474, 42), Size = new Size(36, 34) };
        _btnPresetMore.Click += (_, _) => ShowPresetMoreMenu();

        // Segments Row 2: Shared Inner Word (Reels/Brackets only) + Animation Step Speed
        _row2Lbl = new Label { Font = Theme.FontBody, ForeColor = Theme.TextSecondary, Location = new Point(16, 84), Size = new Size(96, 34), TextAlign = ContentAlignment.MiddleLeft };

        _templateWordWrap = new Panel
        {
            Location = new Point(114, 84),
            Size = new Size(178, 34),
            BackColor = Theme.BgInput
        };
        _templateWordWrap.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.BgCard);
            var r = new RectangleF(0.5f, 0.5f, _templateWordWrap.Width - 2f, _templateWordWrap.Height - 2f);
            using var path = Theme.CreateRoundedPath(r, 7f);
            using var fill = new SolidBrush(Theme.BgInput);
            Color rim = _templateWordBox.Focused ? Theme.Accent : Theme.BorderStrong;
            using var border = new Pen(rim, 1f);
            g.FillPath(fill, path);
            g.DrawPath(border, path);

            string cnt = $"{_templateWordBox.Text.Length}/5";
            TextRenderer.DrawText(g, cnt, Theme.FontSmall,
                new Rectangle(_templateWordWrap.Width - 44, 0, 34, _templateWordWrap.Height),
                Theme.TextMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        _templateWordWrap.Click += (_, _) => _templateWordBox.Focus();

        _templateWordBox = new TextBox
        {
            Width = 120,
            MaxLength = 5,
            BackColor = Theme.BgInput,
            ForeColor = Theme.Accent,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 12f, FontStyle.Bold),
            CharacterCasing = CharacterCasing.Normal,
            TextAlign = HorizontalAlignment.Left
        };
        _templateWordBox.Location = new Point(10, Math.Max(4, (_templateWordWrap.Height - _templateWordBox.PreferredHeight) / 2));
        _templateWordBox.GotFocus += (_, _) => _templateWordWrap.Invalidate();
        _templateWordBox.LostFocus += (_, _) => _templateWordWrap.Invalidate();
        _templateWordBox.TextChanged += (_, _) =>
        {
            _templateWordWrap.Invalidate();
            OnTemplateInnerTextChanged();
        };
        _templateWordWrap.Controls.Add(_templateWordBox);

        _animSpeedStepper = new ModernDelayStepper
        {
            Location = new Point(298, 84),
            Size = new Size(212, 34),
            SideButtonWidth = 34,
            CompactMsOnly = true,
            Minimum = 150,
            Maximum = 1500,
            Step = 50
        };
        _animSpeedStepper.ValueChanged += (_, _) => OnAnimStepSpeedChanged();

        _lcdDelayLbl = new Label { Font = Theme.FontBody, ForeColor = Theme.TextSecondary, Location = new Point(16, 88), Size = new Size(190, 34), TextAlign = ContentAlignment.MiddleLeft };
        _lcdDelayStepper = new ModernDelayStepper
        {
            Location = new Point(214, 88),
            Size = new Size(296, 34),
            Minimum = 500,
            Maximum = 30000,
            Step = 250
        };

        _lcdScreenOffSwitch = new ModernToggleSwitch
        {
            Location = new Point(16, 46),
            Size = new Size(494, 28)
        };
        _lcdScreenOffSwitch.CheckedChanged += (_, _) => UpdateLcdFieldStates();

        _lcdHideIconsSwitch = new ModernToggleSwitch
        {
            Location = new Point(16, 132),
            Size = new Size(494, 28)
        };
        _lcdHideIconsSwitch.CheckedChanged += (_, _) =>
        {
            _hideTopIconsByMode[(int)_lcdModeToggle.SelectedValue] = _lcdHideIconsSwitch.Checked;
            _lcdPreview.HideTopIcons = _lcdHideIconsSwitch.Checked;
        };

        _lcdTextCard.Controls.AddRange([
            _lcdSettingsTitle, _lcdScreenOffSwitch, _lcdTextLbl, _lcdTextWrap,
            _presetLbl, _presetCombo, _btnSavePreset, _btnPresetMore,
            _row2Lbl, _templateWordWrap, _animSpeedStepper,
            _lcdDelayLbl, _lcdDelayStepper, _lcdHideIconsSwitch
        ]);
        page.Controls.AddRange([_lcdModeCard, _lcdTextCard]);
        return page;
    }

    private Panel BuildRgbPage()
    {
        var page = new Panel { BackColor = Theme.BgWindow };
        _rgbStudio = new RgbStudioControl();
        page.Controls.Add(_rgbStudio);
        return page;
    }

    private Panel BuildHotkeysPage()
    {
        var page = new Panel { BackColor = Theme.BgWindow };
        var card = new RoundedCardPanel { Location = new Point(0, 0), Size = new Size(526, 474) };

        _hkTitle = new Label { Font = Theme.FontSection, ForeColor = Theme.TextPrimary, Location = new Point(16, 12), AutoSize = true };
        _hkSub = new Label { Font = Theme.FontSmall, ForeColor = Theme.TextMuted, Location = new Point(16, 34), AutoSize = true };

        _hkToggleOut = new HotkeyCaptureBox();
        _hkPre = new HotkeyCaptureBox();
        _hkPo = new HotkeyCaptureBox();
        _hkToggleOut.BindingChanged += (_, _) => RefreshOutputButtonBadges();
        _hkPre.BindingChanged += (_, _) => RefreshOutputButtonBadges();
        _hkPo.BindingChanged += (_, _) => RefreshOutputButtonBadges();
        _hkLcd = new HotkeyCaptureBox();
        _hkRgb = new HotkeyCaptureBox();
        _hkMute = new HotkeyCaptureBox();
        _hkGainCycle = new HotkeyCaptureBox();
        _hkFilterToggle = new HotkeyCaptureBox();
        _hkReboot = new HotkeyCaptureBox();

        HotkeyCaptureBox[] boxes = [_hkToggleOut, _hkPre, _hkPo, _hkLcd, _hkRgb, _hkMute, _hkGainCycle, _hkFilterToggle, _hkReboot];
        _hkRowLabels.Clear();
        card.Controls.AddRange([_hkTitle, _hkSub]);

        for (int i = 0; i < boxes.Length; i++)
        {
            int y = 60 + i * 40;
            var rowBg = new RoundedCardPanel
            {
                Location = new Point(16, y),
                Size = new Size(494, 35),
                CardColor = Theme.BgElevated,
                BorderColor = Theme.BorderSubtle,
                CornerRadius = 7f
            };
            var lbl = new Label
            {
                Font = Theme.FontBody,
                ForeColor = Theme.TextPrimary,
                BackColor = Theme.BgElevated,
                Location = new Point(12, 3),
                Size = new Size(266, 29),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _hkRowLabels.Add(lbl);

            HotkeyCaptureBox box = boxes[i];
            box.BorderStyle = BorderStyle.None;
            var boxWrap = new RoundedCardPanel
            {
                Location = new Point(284, 5),
                Size = new Size(164, 25),
                CornerRadius = 6f,
                CardColor = Theme.BgInput,
                BorderColor = Theme.BorderSubtle
            };
            box.Location = new Point(8, Math.Max(2, (boxWrap.Height - box.PreferredHeight) / 2));
            box.Size = new Size(boxWrap.Width - 16, box.PreferredHeight);
            void SyncBoxWrapColors()
            {
                Color bg = box.Focused ? Theme.BgElevated : Theme.BgInput;
                box.BackColor = bg;
                boxWrap.CardColor = bg;
                boxWrap.BorderColor = box.Focused ? Theme.Accent : Theme.BorderSubtle;
                boxWrap.Invalidate();
            }
            box.GotFocus += (_, _) => SyncBoxWrapColors();
            box.LostFocus += (_, _) => SyncBoxWrapColors();
            boxWrap.MouseDown += (_, _) => box.Focus();
            boxWrap.Controls.Add(box);

            var clearBtn = new ModernButton
            {
                ShowClearIcon = true,
                Location = new Point(458, 5),
                Size = new Size(24, 24),
                CornerRadius = 6f,
                Variant = ButtonVariant.Secondary
            };
            clearBtn.Click += (_, _) => box.Binding = new HotkeyBinding();

            rowBg.Controls.AddRange([lbl, boxWrap, clearBtn]);
            card.Controls.Add(rowBg);
        }

        _hkResetBtn = new ModernButton
        {
            Location = new Point(16, 426),
            Size = new Size(198, 34),
            Variant = ButtonVariant.Secondary
        };
        _hkResetBtn.Click += (_, _) =>
        {
            var def = new HotkeyConfig();
            _hkToggleOut.Binding = def.ToggleOutput;
            _hkPre.Binding = def.SwitchToPre;
            _hkPo.Binding = def.SwitchToPo;
            _hkLcd.Binding = def.ToggleCustomText;
            _hkRgb.Binding = def.ToggleRgb;
            _hkMute.Binding = def.ToggleMute;
            _hkGainCycle.Binding = def.CycleGain;
            _hkFilterToggle.Binding = def.ToggleFilter;
            _hkReboot.Binding = def.RebootDac;
        };

        card.Controls.Add(_hkResetBtn);
        page.Controls.Add(card);
        return page;
    }

    private Panel BuildDiagnosticsPage()
    {
        var page = new Panel { BackColor = Theme.BgWindow };
        var card = new RoundedCardPanel { Location = new Point(0, 0), Size = new Size(526, 484) };

        // 76px toolbar gives 4px headroom below 32px Row 2 buttons so high-DPI scaling never clips bottom borders
        var toolbar = new Panel
        {
            Location = new Point(16, 14),
            Size = new Size(494, 76),
            BackColor = Theme.BgCard
        };

        ModernButton MakeDiagBtn(int x, int y, int width, ButtonVariant variant = ButtonVariant.Secondary) =>
            new() { Location = new Point(x, y), Size = new Size(width, 32), Variant = variant };

        // Column-aligned 2-row grid across 494px (Col1=126, Col2=114, Col3=114, Col4=116; Row2 Col3+Col4=238)
        _diagRefreshBtn = MakeDiagBtn(0,   0, 126, ButtonVariant.Primary);
        _diagCh0Btn     = MakeDiagBtn(134, 0, 114);
        _diagCh1Btn     = MakeDiagBtn(256, 0, 114);
        _diagMuteBtn    = MakeDiagBtn(378, 0, 116);

        _diagCol01Btn   = MakeDiagBtn(0,   40, 126);
        _diagCol02Btn   = MakeDiagBtn(134, 40, 114);
        _diagFlasherBtn = MakeDiagBtn(256, 40, 238);

        _diagOutput = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Theme.BgInput,
            ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.FontMono,
            Location = new Point(16, 98),
            Size = new Size(494, 372)
        };

        void ShowResult(string actionLine)
        {
            _diagOutput.Text = $"[{DateTime.Now:HH:mm:ss}] {actionLine}"
                + Environment.NewLine + Environment.NewLine
                + Stage0ProbeRunner.BuildStatusReport(_config.Language);
        }

        void ToggleChannelDb(uint ch)
        {
            var audio = new Audio.CoreAudioVolumeService();
            var cur = audio.TryGetEndpointInfo();
            if (cur is null)
            {
                ShowResult(AppStrings.DiagNoEndpoint(_config.Language, ch));
                return;
            }
            float curDb = ch == 0 ? cur.Ch0Db : cur.Ch1Db;
            float nextDb = curDb < -0.5f ? 0.0f : -1.0f;
            bool ok = audio.TrySetChannelVolumeDb(ch, nextDb);
            ShowResult(AppStrings.DiagSentChDb(_config.Language, ch, nextDb, ok));
        }

        _diagRefreshBtn.Click += (_, _) => _diagOutput.Text = Stage0ProbeRunner.BuildStatusReport(_config.Language);
        _diagCh0Btn.Click += (_, _) => ToggleChannelDb(0);
        _diagCh1Btn.Click += (_, _) => ToggleChannelDb(1);
        _diagMuteBtn.Click += (_, _) =>
        {
            var audio = new Audio.CoreAudioVolumeService();
            var cur = audio.TryGetEndpointInfo();
            if (cur is null) { ShowResult(AppStrings.DiagNoEndpoint(_config.Language, null)); return; }
            bool nextMute = !cur.Muted;
            bool ok = audio.TrySetMute(nextMute);
            ShowResult(AppStrings.DiagSentMute(_config.Language, nextMute, ok));
        };
        _diagCol01Btn.Click += (_, _) =>
        {
            new K11HidMonitor().TrySendStage0Col01Probe([0xAA, 0x55, 0x01, 0x02], out string msg, _config.Language);
            ShowResult(msg);
        };
        _diagCol02Btn.Click += (_, _) =>
        {
            new K11HidMonitor().TrySendStage0Col02FeatureProbe([0xBB, 0x66, 0x03, 0x04], out string msg, _config.Language);
            ShowResult(msg);
        };
        _diagFlasherBtn.Click += (_, _) =>
        {
            string localFlasher = Path.Combine(AppContext.BaseDirectory, "GD32_MCU_HID_IAP_Programmer.exe");
            if (!File.Exists(localFlasher))
            {
                ShowResult(AppStrings.DiagFlasherMissing(_config.Language, localFlasher));
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = localFlasher,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            });
            ShowResult(AppStrings.DiagFlasherLaunched(_config.Language));
        };

        toolbar.Controls.AddRange([_diagRefreshBtn, _diagCh0Btn, _diagCh1Btn, _diagMuteBtn, _diagCol01Btn, _diagCol02Btn, _diagFlasherBtn]);
        card.Controls.AddRange([toolbar, _diagOutput]);
        page.Controls.Add(card);
        return page;
    }

    public void ApplyLanguage(AppLanguage lang)
    {
        _config.Language = lang;
        _langToggle.SetSelectedSilently(lang);

        _navButtons[0].Text = AppStrings.NavOutput(lang);
        _navButtons[1].Text = AppStrings.NavLcd(lang);
        _navButtons[2].Text = AppStrings.NavRgb(lang);
        _navButtons[3].Text = AppStrings.NavHotkeys(lang);
        _navButtons[4].Text = AppStrings.NavDiag(lang);
        foreach (var b in _navButtons) b.Invalidate();

        _pageTitleLabel.Text = AppStrings.PageTitle(lang, _activePage);
        SetDeviceStatus(_dacConnected, _audioMuted);

        _cancelBtn.Text = AppStrings.BtnClose(lang);
        UpdateApplyButtonAppearance();
        _saveCloseBtn.Text = AppStrings.BtnSaveClose(lang);

        // Output page
        _outTitle.Text = AppStrings.OutCardTitle(lang);
        _btnToggleOut.Text = AppStrings.OutToggleBtn(lang);
        _btnPreOut.Text = AppStrings.OutPreBtn(lang);
        _btnPoOut.Text = AppStrings.OutPoBtn(lang);

        _dacModesTitle.Text = AppStrings.DacModesCardTitle(lang);
        _gainLbl.Text = AppStrings.GainRowLabel(lang);
        _btnGainLow.Text = AppStrings.GainLowBtn(lang);
        _btnGainMid.Text = AppStrings.GainMidBtn(lang);
        _btnGainHigh.Text = AppStrings.GainHighBtn(lang);
        _filterLbl.Text = AppStrings.FilterRowLabel(lang);
        _btnFilterOs.Text = AppStrings.FilterOsBtn(lang);
        _btnFilterNos.Text = AppStrings.FilterNosBtn(lang);

        _bootTitle.Text = AppStrings.StartupCardTitle(lang);
        _autostartSwitch.Text = AppStrings.AutostartSwitchText(lang);
        _enforceOutputSwitch.Text = AppStrings.StartupSwitchText(lang);
        _startupOutputToggle.UpdateLabels(
            (OutputMode.PO, AppStrings.StartupPo(lang)),
            (OutputMode.PRE, AppStrings.StartupPre(lang)));

        _rebootBtn.Text = AppStrings.RebootBtn(lang);
        _toolTip.SetToolTip(_rebootBtn, AppStrings.UsbHint(lang));

        // LCD page
        _lcdCardTitle.Text = AppStrings.LcdCardTitle(lang);
        _lcdModeToggle.UpdateLabels(
            (CustomLcdMode.Normal, AppStrings.LcdNormal(lang)),
            (CustomLcdMode.Text, AppStrings.LcdTextMode(lang)),
            (CustomLcdMode.Segments, AppStrings.LcdSegmentsMode(lang)));
        _lcdSettingsTitle.Text = AppStrings.LcdSettingsTitle(lang);
        _lcdTextLbl.Text = AppStrings.LcdTextLbl(lang);
        _presetLbl.Text = AppStrings.LcdPresetLbl(lang);
        _btnSavePreset.Text = _savedBadgeActive ? AppStrings.LcdSavePresetDone(lang) : AppStrings.LcdSavePresetBtn(lang);
        _frameCapsule.LabelPrefix = AppStrings.LcdFramePrefix(lang);
        _btnClearFrame.Text = AppStrings.LcdClearSticksBtn(lang);
        _animSpeedStepper.Language = lang;
        foreach (var preset in _editingPresets)
        {
            if (preset.IsBuiltIn && preset.Template != SticksTemplateKind.Custom)
                preset.Name = AppStrings.SticksStarterPresetName(lang, preset.Template);
        }
        RefreshPresetDropdown();
        RefreshFrameCountDropdown(lang);
        RefreshSticksControls();
        _lcdDelayLbl.Text = AppStrings.LcdDelayLbl(lang);
        _lcdDelayStepper.Language = lang;
        _lcdScreenOffSwitch.Text = AppStrings.LcdScreenOff(lang);
        _lcdScreenOffSwitch.Invalidate();
        _lcdHideIconsSwitch.Text = AppStrings.LcdHideIcons(lang);
        _lcdHideIconsSwitch.Invalidate();

        // RGB Studio
        _rgbStudio.ApplyLanguage(lang);

        // Hotkeys page
        _hkTitle.Text = AppStrings.HkCardTitle(lang);
        _hkSub.Text = AppStrings.HkSub(lang);
        if (_hkRowLabels.Count >= 9)
        {
            _hkRowLabels[0].Text = AppStrings.HkRowToggle(lang);
            _hkRowLabels[1].Text = AppStrings.HkRowPre(lang);
            _hkRowLabels[2].Text = AppStrings.HkRowPo(lang);
            _hkRowLabels[3].Text = AppStrings.HkRowLcd(lang);
            _hkRowLabels[4].Text = AppStrings.HkRowRgb(lang);
            _hkRowLabels[5].Text = AppStrings.HkRowMute(lang);
            _hkRowLabels[6].Text = AppStrings.HkRowGainCycle(lang);
            _hkRowLabels[7].Text = AppStrings.HkRowFilterToggle(lang);
            _hkRowLabels[8].Text = AppStrings.HkRowReboot(lang);
        }
        _hkResetBtn.Text = AppStrings.HkResetDefaults(lang);

        // Diagnostics page
        _diagRefreshBtn.Text = AppStrings.DiagRefresh(lang);
        _diagCh0Btn.Text = AppStrings.DiagCh0(lang);
        _diagCh1Btn.Text = AppStrings.DiagCh1(lang);
        _diagMuteBtn.Text = AppStrings.DiagMute(lang);
        _diagCol01Btn.Text = AppStrings.DiagCol01(lang);
        _diagCol02Btn.Text = AppStrings.DiagCol02(lang);
        _diagFlasherBtn.Text = AppStrings.DiagFlasher(lang);
        if (_diagOutput is not null)
            _diagOutput.Text = Stage0ProbeRunner.BuildStatusReport(lang);
    }

    private void LoadValues(GainMode initialPreviewGain, OutputMode? initialPreviewOutput)
    {
        _langToggle.SetSelectedSilently(_config.Language);
        _autostartSwitch.SetCheckedSilently(AutostartManager.IsEnabled());
        _enforceOutputSwitch.SetCheckedSilently(_config.EnforceOutputOnConnect);
        _startupOutputToggle.SetSelectedSilently(_config.PreferredOutput);
        _startupOutputToggle.Enabled = _config.EnforceOutputOnConnect;

        _config.CustomLcd.EnsureSticksPresetsInitialized(_config.Language);
        _editingPresets.Clear();
        foreach (var preset in _config.CustomLcd.SticksPresets)
            _editingPresets.Add(preset.Clone());
        _activePresetIdx = Math.Clamp(_config.CustomLcd.ActiveSticksPresetIndex, 0, Math.Max(0, _editingPresets.Count - 1));

        // Ensure current live DAC masks stay synced inside active custom preset slot if user had edited outside presets
        if (_editingPresets.Count > 0 && _config.CustomLcd.Mode == CustomLcdMode.Segments)
        {
            var ap = _editingPresets[_activePresetIdx];
            if (!ap.IsBuiltIn)
            {
                var curFrames = SticksPreset.NormalizeFrames(_config.CustomLcd.AnimationFrames);
                if (curFrames[0].Any(m => m != 0) || _config.CustomLcd.FrameCount > 1)
                {
                    ap.FrameCount = Math.Clamp(_config.CustomLcd.FrameCount, 1, SticksPreset.MaxFrames);
                    ap.StepIntervalMs = Math.Clamp(_config.CustomLcd.StepIntervalMs, 150, 1500);
                    ap.Frames = curFrames;
                }
            }
        }

        _lcdModeToggle.SetSelectedSilently(_config.CustomLcd.Mode);
        _lcdTextBox.Text = _config.CustomLcd.Text;
        _lcdTextWrap.Invalidate();
        _lcdDelayStepper.SetValueSilently(Math.Clamp(_config.CustomLcd.ActivityRevertDelayMs, 500, 30000));
        _lcdDelayStepper.Language = _config.Language;
        _hideTopIconsByMode[(int)CustomLcdMode.Normal] = _config.CustomLcd.HideTopIconsNormal;
        _hideTopIconsByMode[(int)CustomLcdMode.Text] = _config.CustomLcd.HideTopIconsText;
        _hideTopIconsByMode[(int)CustomLcdMode.Segments] = _config.CustomLcd.HideTopIconsSegments;
        _lcdScreenOffSwitch.SetCheckedSilently(_config.CustomLcd.ScreenOff);
        _lcdHideIconsSwitch.SetCheckedSilently(_config.CustomLcd.GetHideTopIcons(_config.CustomLcd.Mode));
        _lcdPreview.PreviewMode = _config.CustomLcd.Mode;
        _lcdPreview.CustomText = _config.CustomLcd.Text;
        _lcdPreview.ScreenOff = _config.CustomLcd.Mode == CustomLcdMode.Normal && _config.CustomLcd.ScreenOff;
        _lcdPreview.HideTopIcons = _config.CustomLcd.GetHideTopIcons(_config.CustomLcd.Mode);
        _lcdPreview.PreviewOutput = initialPreviewOutput ?? (_config.PreferredOutput == OutputMode.PO ? OutputMode.PO : OutputMode.PRE);
        _lcdPreview.PreviewGain = initialPreviewGain;
        LoadActivePresetIntoEditor(startPlayback: true);
        UpdateLcdFieldStates();

        _rgbStudio.LoadFromConfig(_config.Rgb);

        _hkToggleOut.Binding = _config.Hotkeys.ToggleOutput;
        _hkPre.Binding = _config.Hotkeys.SwitchToPre;
        _hkPo.Binding = _config.Hotkeys.SwitchToPo;
        _hkLcd.Binding = _config.Hotkeys.ToggleCustomText;
        _hkRgb.Binding = _config.Hotkeys.ToggleRgb;
        _hkMute.Binding = _config.Hotkeys.ToggleMute;
        _hkGainCycle.Binding = _config.Hotkeys.CycleGain;
        _hkFilterToggle.Binding = _config.Hotkeys.ToggleFilter;
        _hkReboot.Binding = _config.Hotkeys.RebootDac;
        RefreshOutputButtonBadges();

        _diagOutput.Text = Stage0ProbeRunner.BuildStatusReport(_config.Language);
    }

    private SticksPreset CurrentPreset => _editingPresets[_activePresetIdx];

    private void RefreshPresetDropdown()
    {
        if (_editingPresets.Count > 0)
            _presetCombo.SetItems(_editingPresets.Select(p => p.Name).ToArray(), _activePresetIdx);
    }

    private void RefreshFrameCountDropdown(AppLanguage lang)
    {
        var items = new (string ButtonLabel, string MenuLabel)[SticksPreset.MaxFrames];
        for (int n = 1; n <= SticksPreset.MaxFrames; n++)
            items[n - 1] = (AppStrings.LcdFrameCountShort(lang, n), AppStrings.LcdFrameCountMenu(lang, n));
        int idx = _editingPresets.Count > 0
            ? Math.Clamp(CurrentPreset.FrameCount - 1, 0, SticksPreset.MaxFrames - 1)
            : 0;
        _frameCountCombo.SetItems(items, idx);
    }

    private void LoadActivePresetIntoEditor(bool startPlayback)
    {
        if (_editingPresets.Count == 0)
            return;
        var p = CurrentPreset;
        p.Normalize();
        _activeFrameIdx = 0;
        _animPlaybackIdx = 0;
        _animPlaying = startPlayback && p.FrameCount > 1;
        _animSpeedStepper.SetValueSilently(p.StepIntervalMs);

        _updatingWordBox = true;
        _templateWordBox.Text = p.InnerText;
        _updatingWordBox = false;
        _templateWordWrap.Invalidate();

        _lcdPreview.SegmentMasks = p.Frames[0];
        RefreshPresetDropdown();
        RefreshFrameCountDropdown(_config.Language);
        RefreshSticksControls();
        SyncAnimPreviewPlayback();
    }

    private bool _updatingWordBox;

    private void RefreshSticksControls()
    {
        if (_editingPresets.Count == 0)
            return;
        var p = CurrentPreset;
        bool isBuiltIn = p.IsBuiltIn;
        bool hasInnerWord = SticksTemplateEngine.SupportsInnerText(p.Template);
        bool isSegments = _lcdModeToggle.SelectedValue == CustomLcdMode.Segments;

        _lcdPreview.InteractiveSegments = isSegments && !isBuiltIn;
        _toolTip.SetToolTip(_lcdPreview, (isSegments && isBuiltIn) ? AppStrings.LcdStockLockedHint(_config.Language) : null);

        _row2Lbl.Text = hasInnerWord
            ? AppStrings.LcdWordAndSpeedLbl(_config.Language)
            : AppStrings.LcdSpeedOnlyLbl(_config.Language);

        if (hasInnerWord)
        {
            _row2Lbl.Size = new Size(96, 34);
            _templateWordWrap.Visible = isSegments;
            _animSpeedStepper.Location = new Point(298, 84);
            _animSpeedStepper.Size = new Size(212, 34);
            _animSpeedStepper.SideButtonWidth = 34;
        }
        else
        {
            _row2Lbl.Size = new Size(190, 34);
            _templateWordWrap.Visible = false;
            _animSpeedStepper.Location = new Point(214, 84);
            _animSpeedStepper.Size = new Size(296, 34);
            _animSpeedStepper.SideButtonWidth = 40;
        }
        _animSpeedStepper.Invalidate();

        _btnAnimPlay.Enabled = p.FrameCount > 1;
        _btnAnimPlay.PlayGlyph = _animPlaying && p.FrameCount > 1 ? PlayGlyphKind.Pause : PlayGlyphKind.Play;

        int showIdx = _animPlaying ? _animPlaybackIdx : _activeFrameIdx;
        _frameCapsule.ActiveCount = p.FrameCount;
        _frameCapsule.SelectedIndex = Math.Clamp(showIdx, 0, p.FrameCount - 1);
        _frameCountCombo.SetSelectedIndexSilently(p.FrameCount - 1);
        _frameCountCombo.Enabled = !isBuiltIn;
        _btnClearFrame.Enabled = !isBuiltIn;
        _animSpeedStepper.Enabled = p.FrameCount > 1;
    }

    private void SyncAnimPreviewPlayback()
    {
        bool shouldRun = _activePage == 1
            && _lcdModeToggle.SelectedValue == CustomLcdMode.Segments
            && _editingPresets.Count > 0
            && CurrentPreset.FrameCount > 1
            && _animPlaying;
        if (!shouldRun)
        {
            _animTimer.Stop();
            return;
        }
        _animTimer.Interval = Math.Clamp(CurrentPreset.StepIntervalMs, 150, 1500);
        if (!_animTimer.Enabled)
            _animTimer.Start();
    }

    private void OnAnimPreviewTick()
    {
        if (_editingPresets.Count == 0 || CurrentPreset.FrameCount <= 1 || !_animPlaying)
        {
            _animTimer.Stop();
            return;
        }
        _animPlaybackIdx = (_animPlaybackIdx + 1) % CurrentPreset.FrameCount;
        _lcdPreview.SegmentMasks = CurrentPreset.Frames[_animPlaybackIdx];
        _frameCapsule.SelectedIndex = _animPlaybackIdx;
    }

    private void OnPreviewSegmentMasksEdited()
    {
        if (_editingPresets.Count == 0 || CurrentPreset.IsBuiltIn)
            return;
        if (_animPlaying)
        {
            _activeFrameIdx = Math.Clamp(_animPlaybackIdx, 0, CurrentPreset.FrameCount - 1);
            _animPlaying = false;
            _animTimer.Stop();
        }
        Array.Copy(_lcdPreview.SegmentMasks, CurrentPreset.Frames[_activeFrameIdx], 7);
        if (CurrentPreset.Template != SticksTemplateKind.Custom)
        {
            CurrentPreset.Template = SticksTemplateKind.Custom;
            CurrentPreset.InnerText = "";
        }
        RefreshSticksControls();
    }

    private void SelectPresetIndex(int index)
    {
        if (index < 0 || index >= _editingPresets.Count)
            return;
        _activePresetIdx = index;
        LoadActivePresetIntoEditor(startPlayback: true);
    }

    private void ShowPresetMoreMenu()
    {
        _presetMoreMenu?.Dispose();
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            BackColor = Theme.BgElevated,
            ForeColor = Theme.TextPrimary,
            Font = Theme.FontBody
        };
        AppLanguage lang = _config.Language;
        bool hasPreset = _editingPresets.Count > 0;
        bool isBuiltIn = hasPreset && CurrentPreset.IsBuiltIn;

        var dupItem = new ToolStripMenuItem(AppStrings.LcdMenuDuplicate(lang, isBuiltIn))
        {
            Enabled = hasPreset,
            ForeColor = hasPreset ? Theme.TextPrimary : Theme.TextMuted
        };
        dupItem.Click += (_, _) => DuplicateActivePreset();
        menu.Items.Add(dupItem);

        var newItem = new ToolStripMenuItem(AppStrings.LcdMenuNewPreset(lang)) { ForeColor = Theme.TextPrimary };
        newItem.Click += (_, _) => CreateBlankPreset();
        menu.Items.Add(newItem);

        if (isBuiltIn)
        {
            var resetItem = new ToolStripMenuItem(AppStrings.LcdMenuResetPreset(lang)) { ForeColor = Theme.TextPrimary };
            resetItem.Click += (_, _) => ResetActiveStockPreset();
            menu.Items.Add(resetItem);
        }

        bool canTemplate = hasPreset && !isBuiltIn;
        var tplRoot = new ToolStripMenuItem(AppStrings.LcdMenuTemplate(lang))
        {
            Enabled = canTemplate,
            ForeColor = canTemplate ? Theme.TextPrimary : Theme.TextMuted
        };
        if (tplRoot.DropDown is ToolStripDropDownMenu sub)
        {
            sub.Renderer = new DarkMenuRenderer();
            sub.ShowImageMargin = false;
            sub.BackColor = Theme.BgElevated;
            sub.ForeColor = Theme.TextPrimary;
            sub.Font = Theme.FontBody;
        }
        SticksTemplateKind? activeKind = hasPreset ? CurrentPreset.Template : null;
        foreach (var kind in SticksTemplateEngine.OrderedTemplates)
        {
            var k = kind;
            bool isCur = activeKind == k;
            var item = new ToolStripMenuItem(AppStrings.LcdTemplateName(lang, k))
            {
                ForeColor = isCur ? Theme.Accent : Theme.TextPrimary,
                Font = isCur ? Theme.FontBodyBold : Theme.FontBody
            };
            item.Click += (_, _) => ApplyTemplateToActivePreset(k);
            tplRoot.DropDownItems.Add(item);
        }
        menu.Items.Add(tplRoot);

        bool canRename = hasPreset && !isBuiltIn;
        var renameItem = new ToolStripMenuItem(AppStrings.LcdMenuRename(lang))
        {
            Enabled = canRename,
            ForeColor = canRename ? Theme.TextPrimary : Theme.TextMuted
        };
        renameItem.Click += (_, _) => RenameActivePreset();
        menu.Items.Add(renameItem);

        menu.Items.Add(new ToolStripSeparator());

        bool canDelete = hasPreset && !isBuiltIn && _editingPresets.Count > 1;
        var deleteItem = new ToolStripMenuItem(AppStrings.LcdMenuDelete(lang))
        {
            Enabled = canDelete,
            ForeColor = canDelete ? Theme.Danger : Theme.TextMuted
        };
        deleteItem.Click += (_, _) => DeleteActivePreset();
        menu.Items.Add(deleteItem);

        _presetMoreMenu = menu;
        menu.Show(_btnPresetMore, new Point(0, _btnPresetMore.Height + 4));
    }

    private void ApplyTemplateToActivePreset(SticksTemplateKind kind)
    {
        if (_editingPresets.Count == 0 || CurrentPreset.IsBuiltIn)
            return;
        var p = CurrentPreset;
        p.Template = kind;
        if (kind == SticksTemplateKind.Custom)
        {
            p.InnerText = "";
            RefreshSticksControls();
            return;
        }

        string word = SticksTemplateEngine.SupportsInnerText(kind)
            ? (string.IsNullOrWhiteSpace(p.InnerText) ? SticksTemplateEngine.DefaultInnerText(kind) : p.InnerText)
            : "";
        p.InnerText = CustomLcdConfig.SanitizeSegment(word, 5).TrimEnd();
        var (frames, count, stepMs) = SticksTemplateEngine.Build(kind, p.InnerText);
        p.Frames = frames;
        p.FrameCount = count;
        p.StepIntervalMs = stepMs;
        LoadActivePresetIntoEditor(startPlayback: true);
    }

    private void OnTemplateInnerTextChanged()
    {
        if (_updatingWordBox || _editingPresets.Count == 0)
            return;
        var p = CurrentPreset;
        if (!SticksTemplateEngine.SupportsInnerText(p.Template))
            return;
        p.InnerText = CustomLcdConfig.SanitizeSegment(_templateWordBox.Text, 5).TrimEnd();
        var (frames, count, _) = SticksTemplateEngine.Build(p.Template, p.InnerText);
        p.Frames = frames;
        p.FrameCount = count;
        int showIdx = _animPlaying ? _animPlaybackIdx : _activeFrameIdx;
        _lcdPreview.SegmentMasks = p.Frames[Math.Clamp(showIdx, 0, count - 1)];
    }

    private void ClearActiveEditedFrame()
    {
        if (_editingPresets.Count == 0 || CurrentPreset.IsBuiltIn)
            return;
        if (_animPlaying)
        {
            _activeFrameIdx = Math.Clamp(_animPlaybackIdx, 0, CurrentPreset.FrameCount - 1);
            _animPlaying = false;
            _animTimer.Stop();
        }
        Array.Clear(CurrentPreset.Frames[_activeFrameIdx], 0, 7);
        if (CurrentPreset.Template != SticksTemplateKind.Custom)
        {
            CurrentPreset.Template = SticksTemplateKind.Custom;
            CurrentPreset.InnerText = "";
        }
        _lcdPreview.SegmentMasks = CurrentPreset.Frames[_activeFrameIdx];
        RefreshSticksControls();
    }

    private void ToggleAnimPreviewPlayback()
    {
        if (_editingPresets.Count == 0 || CurrentPreset.FrameCount <= 1)
            return;
        _animPlaying = !_animPlaying;
        if (_animPlaying)
            _animPlaybackIdx = _activeFrameIdx;
        else
            _activeFrameIdx = Math.Clamp(_animPlaybackIdx, 0, CurrentPreset.FrameCount - 1);
        _lcdPreview.SegmentMasks = CurrentPreset.Frames[_animPlaying ? _animPlaybackIdx : _activeFrameIdx];
        RefreshSticksControls();
        SyncAnimPreviewPlayback();
    }

    private void SelectEditedFrame(int idx)
    {
        if (_editingPresets.Count == 0 || idx < 0 || idx >= CurrentPreset.FrameCount)
            return;
        _animPlaying = false;
        _animTimer.Stop();
        _activeFrameIdx = idx;
        _animPlaybackIdx = idx;
        _lcdPreview.SegmentMasks = CurrentPreset.Frames[_activeFrameIdx];
        RefreshSticksControls();
    }

    private void SetActivePresetFrameCount(int nextCount)
    {
        if (_editingPresets.Count == 0 || CurrentPreset.IsBuiltIn)
            return;
        var p = CurrentPreset;
        int next = Math.Clamp(nextCount, 1, SticksPreset.MaxFrames);
        if (next == p.FrameCount)
            return;

        _animPlaying = false;
        _animTimer.Stop();
        if (next > p.FrameCount)
        {
            // Duplicate current active frame across newly added slots so editing starts from visible sticks
            int src = Math.Clamp(_activeFrameIdx, 0, p.FrameCount - 1);
            for (int f = p.FrameCount; f < next; f++)
                Array.Copy(p.Frames[src], p.Frames[f], 7);
            _activeFrameIdx = next - 1;
        }
        else if (_activeFrameIdx >= next)
        {
            _activeFrameIdx = next - 1;
        }
        p.FrameCount = next;
        _lcdPreview.SegmentMasks = p.Frames[_activeFrameIdx];
        RefreshSticksControls();
    }

    private void OnAnimStepSpeedChanged()
    {
        if (_editingPresets.Count == 0)
            return;
        CurrentPreset.StepIntervalMs = _animSpeedStepper.Value;
        SyncAnimPreviewPlayback();
    }

    private void SaveActivePresetToConfig()
    {
        if (_editingPresets.Count == 0)
            return;
        CurrentPreset.Normalize();
        PersistEditingPresets();
        _savedBadgeActive = true;
        _btnSavePreset.Text = AppStrings.LcdSavePresetDone(_config.Language);
        _saveFeedbackTimer.Stop();
        _saveFeedbackTimer.Start();
    }

    private string? PromptPresetName(string title, string defaultName)
    {
        using var dlg = new DarkPresetNameDialog(
            title,
            AppStrings.LcdNewPresetPrompt(_config.Language),
            defaultName,
            _config.Language);
        if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.PresetName))
            return null;
        return dlg.PresetName;
    }

    private void DuplicateActivePreset()
    {
        if (_editingPresets.Count == 0)
            return;
        string defName = AppStrings.LcdDuplicateDefaultName(_config.Language, CurrentPreset.Name);
        string? name = PromptPresetName(AppStrings.LcdDuplicatePresetTitle(_config.Language), defName);
        if (name is null)
            return;

        var copy = CurrentPreset.Clone();
        copy.Name = name;
        copy.IsBuiltIn = false;
        copy.Normalize();
        AddAndSelectCustomPreset(copy);
    }

    private void CreateBlankPreset()
    {
        int customIdx = _editingPresets.Count(p => !p.IsBuiltIn) + 1;
        string defName = AppStrings.SticksStarterCustomName(_config.Language, customIdx);
        string? name = PromptPresetName(AppStrings.LcdNewPresetTitle(_config.Language), defName);
        if (name is null)
            return;

        var blank = new SticksPreset
        {
            Name = name,
            IsBuiltIn = false,
            Template = SticksTemplateKind.Custom,
            FrameCount = 1,
            StepIntervalMs = 300,
            Frames = SticksPreset.CreateEmptyFrames()
        };
        AddAndSelectCustomPreset(blank);
    }

    private void AddAndSelectCustomPreset(SticksPreset preset)
    {
        _editingPresets.Add(preset);
        _activePresetIdx = _editingPresets.Count - 1;
        PersistEditingPresets();
        LoadActivePresetIntoEditor(startPlayback: preset.FrameCount > 1);
    }

    private void ResetActiveStockPreset()
    {
        if (_editingPresets.Count == 0 || !CurrentPreset.IsBuiltIn || CurrentPreset.Template == SticksTemplateKind.Custom)
            return;
        _editingPresets[_activePresetIdx] = SticksPreset.FromTemplate(
            AppStrings.SticksStarterPresetName(_config.Language, CurrentPreset.Template),
            CurrentPreset.Template,
            isBuiltIn: true);
        PersistEditingPresets();
        LoadActivePresetIntoEditor(startPlayback: true);
    }

    private void RenameActivePreset()
    {
        if (_editingPresets.Count == 0 || CurrentPreset.IsBuiltIn)
            return;
        string? name = PromptPresetName(AppStrings.LcdRenamePresetTitle(_config.Language), CurrentPreset.Name);
        if (name is null)
            return;
        CurrentPreset.Name = name;
        PersistEditingPresets();
        RefreshPresetDropdown();
    }

    private void DeleteActivePreset()
    {
        if (_editingPresets.Count <= 1 || CurrentPreset.IsBuiltIn)
            return;
        _editingPresets.RemoveAt(_activePresetIdx);
        _activePresetIdx = Math.Clamp(_activePresetIdx, 0, _editingPresets.Count - 1);
        PersistEditingPresets();
        LoadActivePresetIntoEditor(startPlayback: CurrentPreset.FrameCount > 1);
    }

    private void PersistEditingPresets()
    {
        var snapshot = _editingPresets.Select(p => p.Clone()).ToList();
        _onPresetsPersisted(snapshot, _activePresetIdx);
    }

    private void UpdateLcdFieldStates()
    {
        CustomLcdMode mode = _lcdModeToggle.SelectedValue;
        bool isNormal = mode == CustomLcdMode.Normal;
        bool isText = mode == CustomLcdMode.Text;
        bool isSegments = mode == CustomLcdMode.Segments;
        bool screenOffActive = isNormal && _lcdScreenOffSwitch.Checked;

        _lcdModeCard.Height = isSegments ? 248 : 204;
        _btnAnimPlay.Visible = isSegments;
        _frameCapsule.Visible = isSegments;
        _frameCountCombo.Visible = isSegments;
        _btnClearFrame.Visible = isSegments;

        _lcdScreenOffSwitch.Visible = isNormal;
        _lcdTextLbl.Visible = isText;
        _lcdTextWrap.Visible = isText;
        _lcdTextBox.Enabled = isText;
        _lcdTextBox.ForeColor = isText ? Theme.Accent : Theme.TextMuted;
        _lcdTextWrap.Invalidate();

        _presetLbl.Visible = isSegments;
        _presetCombo.Visible = isSegments;
        _btnSavePreset.Visible = isSegments;
        _btnPresetMore.Visible = isSegments;
        _row2Lbl.Visible = isSegments;
        _animSpeedStepper.Visible = isSegments;

        if (isSegments)
        {
            _lcdTextCard.Location = new Point(0, 258);
            _lcdTextCard.Height = 214;
            _lcdDelayLbl.Location = new Point(16, 126);
            _lcdDelayStepper.Location = new Point(214, 126);
            _lcdHideIconsSwitch.Location = new Point(16, 170);
            RefreshSticksControls();
        }
        else
        {
            _templateWordWrap.Visible = false;
            _lcdTextCard.Location = new Point(0, 214);
            _lcdTextCard.Height = 174;
            _lcdDelayLbl.Location = new Point(16, 88);
            _lcdDelayStepper.Location = new Point(214, 88);
            _lcdHideIconsSwitch.Location = new Point(16, 132);
        }

        _lcdHideIconsSwitch.SetCheckedSilently(_hideTopIconsByMode[(int)mode]);
        _lcdPreview.HideTopIcons = _hideTopIconsByMode[(int)mode];
        _lcdPreview.ScreenOff = screenOffActive;

        _lcdDelayStepper.Enabled = isText || isSegments || screenOffActive;
    }

    private void SaveValues()
    {
        _config.Language = _langToggle.SelectedValue;
        _config.EnforceOutputOnConnect = _enforceOutputSwitch.Checked;
        _config.PreferredOutput = _startupOutputToggle.SelectedValue;

        _config.Hotkeys.ToggleOutput = _hkToggleOut.Binding;
        _config.Hotkeys.SwitchToPre = _hkPre.Binding;
        _config.Hotkeys.SwitchToPo = _hkPo.Binding;
        _config.Hotkeys.ToggleCustomText = _hkLcd.Binding;
        _config.Hotkeys.ToggleRgb = _hkRgb.Binding;
        _config.Hotkeys.ToggleMute = _hkMute.Binding;
        _config.Hotkeys.CycleGain = _hkGainCycle.Binding;
        _config.Hotkeys.ToggleFilter = _hkFilterToggle.Binding;
        _config.Hotkeys.RebootDac = _hkReboot.Binding;

        _config.CustomLcd.Mode = _lcdModeToggle.SelectedValue;
        _config.CustomLcd.Text = _lcdTextBox.Text;
        _config.CustomLcd.SticksPresets = _editingPresets.Select(p => p.Clone()).ToList();
        _config.CustomLcd.ActiveSticksPresetIndex = _activePresetIdx;
        if (_editingPresets.Count > 0)
            _config.CustomLcd.ApplyPresetToLive(CurrentPreset);
        _config.CustomLcd.ActivityRevertDelayMs = _lcdDelayStepper.Value;
        _config.CustomLcd.ScreenOff = _lcdScreenOffSwitch.Checked;
        _config.CustomLcd.HideTopIconsNormal = _hideTopIconsByMode[(int)CustomLcdMode.Normal];
        _config.CustomLcd.HideTopIconsText = _hideTopIconsByMode[(int)CustomLcdMode.Text];
        _config.CustomLcd.HideTopIconsSegments = _hideTopIconsByMode[(int)CustomLcdMode.Segments];

        _rgbStudio.WriteToConfig(_config.Rgb);
        RefreshOutputButtonBadges();
    }

    private void ApplyCurrentEdits(bool showFeedback = true)
    {
        SaveValues();
        int seq = ++_applyFeedbackSeq;
        _onApplyAsync(CloneConfigSnapshot(_config), ok =>
        {
            if (!showFeedback)
                return;

            void Deliver()
            {
                if (IsDisposed || Disposing || seq != _applyFeedbackSeq)
                    return;

                _applyFeedbackState = ok
                    ? ApplyFeedbackState.Success
                    : (_dacConnected ? ApplyFeedbackState.Error : ApplyFeedbackState.Offline);
                UpdateApplyButtonAppearance();
                _applyFeedbackTimer.Stop();
                _applyFeedbackTimer.Start();
            }

            if (IsDisposed || Disposing)
                return;

            // Marshal to the Form's UI thread so _applyFeedbackTimer ticks on the WinForms message pump.
            if (InvokeRequired && IsHandleCreated)
            {
                try { BeginInvoke((Action)Deliver); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
            else
            {
                Deliver();
            }
        });
    }

    private void ClearApplyFeedback()
    {
        _applyFeedbackTimer.Stop();
        if (_applyFeedbackState == ApplyFeedbackState.Normal)
            return;

        _applyFeedbackState = ApplyFeedbackState.Normal;
        if (!IsDisposed && _applyBtn is not null)
            UpdateApplyButtonAppearance();
    }

    private void UpdateApplyButtonAppearance()
    {
        AppLanguage lang = _config.Language;
        switch (_applyFeedbackState)
        {
            case ApplyFeedbackState.Success:
                _applyBtn.Text = AppStrings.BtnApplyDone(lang);
                _applyBtn.BorderColorOverride = Theme.Success;
                _applyBtn.ForeColorOverride = Color.FromArgb(134, 239, 172);
                break;
            case ApplyFeedbackState.Offline:
                _applyBtn.Text = AppStrings.BtnApplyOffline(lang);
                _applyBtn.BorderColorOverride = Theme.Danger;
                _applyBtn.ForeColorOverride = Color.FromArgb(254, 202, 202);
                break;
            case ApplyFeedbackState.Error:
                _applyBtn.Text = AppStrings.BtnApplyError(lang);
                _applyBtn.BorderColorOverride = Theme.Danger;
                _applyBtn.ForeColorOverride = Color.FromArgb(254, 202, 202);
                break;
            default:
                _applyBtn.Text = AppStrings.BtnApply(lang);
                _applyBtn.BorderColorOverride = null;
                _applyBtn.ForeColorOverride = null;
                break;
        }
    }

    private void RefreshOutputButtonBadges()
    {
        if (_btnToggleOut is null || _hkToggleOut is null)
            return;

        HotkeyBinding toggleOut = _hkToggleOut.Binding;
        HotkeyBinding switchPre = _hkPre.Binding;
        HotkeyBinding switchPo = _hkPo.Binding;
        _btnToggleOut.BadgeText = toggleOut.IsConfigured ? toggleOut.ToString() : null;
        _btnPreOut.BadgeText = switchPre.IsConfigured ? switchPre.ToString() : null;
        _btnPoOut.BadgeText = switchPo.IsConfigured ? switchPo.ToString() : null;
        _btnToggleOut.Invalidate();
        _btnPreOut.Invalidate();
        _btnPoOut.Invalidate();
    }

    private static HotkeyConfig CloneHotkeys(HotkeyConfig h) => new()
    {
        ToggleOutput = new HotkeyBinding { Modifiers = h.ToggleOutput.Modifiers, Key = h.ToggleOutput.Key },
        SwitchToPo = new HotkeyBinding { Modifiers = h.SwitchToPo.Modifiers, Key = h.SwitchToPo.Key },
        SwitchToPre = new HotkeyBinding { Modifiers = h.SwitchToPre.Modifiers, Key = h.SwitchToPre.Key },
        ToggleCustomText = new HotkeyBinding { Modifiers = h.ToggleCustomText.Modifiers, Key = h.ToggleCustomText.Key },
        ToggleRgb = new HotkeyBinding { Modifiers = h.ToggleRgb.Modifiers, Key = h.ToggleRgb.Key },
        ToggleMute = new HotkeyBinding { Modifiers = h.ToggleMute.Modifiers, Key = h.ToggleMute.Key },
        CycleGain = new HotkeyBinding { Modifiers = h.CycleGain.Modifiers, Key = h.CycleGain.Key },
        ToggleFilter = new HotkeyBinding { Modifiers = h.ToggleFilter.Modifiers, Key = h.ToggleFilter.Key },
        RebootDac = new HotkeyBinding { Modifiers = h.RebootDac.Modifiers, Key = h.RebootDac.Key }
    };

    private static CustomLcdConfig CloneLcd(CustomLcdConfig l) => new()
    {
        Mode = l.Mode,
        LastCustomMode = l.LastCustomMode,
        HideTopIconsNormal = l.HideTopIconsNormal,
        HideTopIconsText = l.HideTopIconsText,
        HideTopIconsSegments = l.HideTopIconsSegments,
        ScreenOff = l.ScreenOff,
        Text = l.Text,
        SegmentMasks = l.SegmentMasks.ToArray(),
        FrameCount = l.FrameCount,
        StepIntervalMs = l.StepIntervalMs,
        AnimationFrames = SticksPreset.NormalizeFrames(l.AnimationFrames),
        ActiveSticksPresetIndex = l.ActiveSticksPresetIndex,
        SticksPresets = l.SticksPresets.Select(p => p.Clone()).ToList(),
        ActivityRevertDelayMs = l.ActivityRevertDelayMs
    };

    private static RgbConfig CloneRgb(RgbConfig r) => new()
    {
        State = r.State,
        Effect = r.Effect,
        Brightness = r.Brightness,
        ZoneHexColors = r.ZoneHexColors.ToArray()
    };

    private static TrayConfig CloneConfigSnapshot(TrayConfig src) => new()
    {
        ConfigVersion = src.ConfigVersion,
        Language = src.Language,
        PreferredOutput = src.PreferredOutput,
        EnforceOutputOnConnect = src.EnforceOutputOnConnect,
        Hotkeys = CloneHotkeys(src.Hotkeys),
        CustomLcd = CloneLcd(src.CustomLcd),
        Rgb = CloneRgb(src.Rgb)
    };
}
