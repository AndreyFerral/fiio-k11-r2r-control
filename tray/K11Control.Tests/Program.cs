using K11Control.Audio;
using K11Control.Config;
using K11Control.Protocol;
using K11Control.Stage0;
using K11Control.UI;

int failures = 0;

void Assert(bool condition, string message)
{
    if (!condition)
    {
        Console.Error.WriteLine($"FAIL: {message}");
        failures++;
    }
    else
    {
        Console.WriteLine($"PASS: {message}");
    }
}

// 1. Supported LCD font table length = 43 exact match to M79 0x0800DB68
Assert(CustomLcdConfig.SupportedGlyphs.Length == 43,
    $"SupportedGlyphs has exact 43 chars (got {CustomLcdConfig.SupportedGlyphs.Length})");

// 2. Sanitization & single 7-char formatting
var lcd = new CustomLcdConfig { Text = "checnep" };
Assert(lcd.Format7Chars() == "CHECNEP", $"Single 7-char format sanitizes to 'CHECNEP' (got '{lcd.Format7Chars()}')");
var lcdShort = new CustomLcdConfig { Text = "k11!" };
Assert(lcdShort.Format7Chars() == "K11    ", $"Short text right-pads to 7 chars (got '{lcdShort.Format7Chars()}')");
Assert(CustomLcdConfig.SanitizeSegment("di\\<9", 4) == "di\\<", "Lowercase 'd'/'i' preserved accurately");

// 2b. M79 14-segment font table (exact hardware segment masks from 0x0800DB68)
ushort maskV = K11Control.UI.K11LcdFont.GetSegmentMask('V');
ushort expectedV = (1 << K11Control.UI.K11LcdFont.SegLeftTop)
                 | (1 << K11Control.UI.K11LcdFont.SegLeftBottom)
                 | (1 << K11Control.UI.K11LcdFont.SegDiagBotLeft)
                 | (1 << K11Control.UI.K11LcdFont.SegDiagTopRight);
Assert(maskV == expectedV, $"'V' matches M79 left-vertical + slash mask (0x{maskV:X4} == 0x{expectedV:X4})");
Assert(K11Control.UI.K11LcdFont.GetSegmentMask('I') != K11Control.UI.K11LcdFont.GetSegmentMask('i'),
    "'I' (serif + center) differs from 'i' (right vertical only)");
Assert(K11Control.UI.K11LcdFont.GetSegmentMask('p') == K11Control.UI.K11LcdFont.GetSegmentMask('P'),
    "Lowercase 'p' resolves identically to uppercase 'P'");
Assert(K11Control.UI.K11LcdFont.GetSegmentMask('d') != K11Control.UI.K11LcdFont.GetSegmentMask('D'),
    "Lowercase 'd' differs from uppercase 'D'");
Assert(K11Control.UI.K11LcdFont.GetSegmentMask('J') != 0
    && K11Control.UI.K11LcdFont.RequiresDirectMaskCommand("JOY"),
    "'J' uses fixed non-zero 14-segment mask and automatically selects direct mask command (0x07)");
Assert(K11Control.UI.K11LcdFont.RequiresDirectMaskCommand("ПРИВЕТ")
    && K11Control.UI.K11LcdFont.GetSegmentMask('п') == K11Control.UI.K11LcdFont.GetSegmentMask('П')
    && K11Control.UI.K11LcdFont.GetSegmentMask('Я') != 0
    && K11Control.UI.K11LcdFont.GetSegmentMask('Ы') != 0,
    "Cyrillic letters resolve case-insensitively to non-zero 14-segment masks requiring cmd 0x07");
Assert(!K11Control.UI.K11LcdFont.RequiresDirectMaskCommand("SPK R2R"),
    "Stock-M79 compatible Latin text keeps legacy cmd 0x02 compatibility");

// 3. Default hotkeys (Alt+1..Alt+5)
var defHk = new HotkeyConfig();
Assert(defHk.ToggleOutput.ToString() == "Alt+1", $"Default ToggleOutput hotkey is Alt+1 (got '{defHk.ToggleOutput}')");
Assert(defHk.SwitchToPre.ToString() == "Alt+2", $"Default SwitchToPre hotkey is Alt+2 (got '{defHk.SwitchToPre}')");
Assert(defHk.SwitchToPo.ToString() == "Alt+3", $"Default SwitchToPo hotkey is Alt+3 (got '{defHk.SwitchToPo}')");
Assert(defHk.ToggleCustomText.ToString() == "Alt+4", $"Default ToggleCustomText hotkey is Alt+4 (got '{defHk.ToggleCustomText}')");
Assert(defHk.ToggleRgb.ToString() == "Alt+5", $"Default ToggleRgb hotkey is Alt+5 (got '{defHk.ToggleRgb}')");
Assert(!defHk.ToggleMute.IsConfigured && defHk.ToggleMute.ToString() == "(None)", "Default ToggleMute hotkey is unset ((None))");

// 4. Config JSON roundtrip, V1 -> V2 migrations (LCD, RGB Off/On/Custom, Hotkeys), and LO safety coercion
string tempDir = Path.Combine(Path.GetTempPath(), $"k11_test_{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDir);
try
{
    string cfgPath = Path.Combine(tempDir, "config.json");
    var cfg = new TrayConfig { PreferredOutput = OutputMode.PRE, Language = AppLanguage.En };
    cfg.CustomLcd.Enabled = true;
    cfg.CustomLcd.Text = "CHECNEP";
    cfg.Rgb.State = RgbStateMode.Custom;
    cfg.Rgb.Effect = RgbEffectMode.Chase;
    cfg.Hotkeys.ToggleMute = new HotkeyBinding { Modifiers = HotkeyModifiers.Alt, Key = Keys.M };
    cfg.Save(cfgPath);

    var reloaded = TrayConfig.Load(cfgPath);
    Assert(reloaded.PreferredOutput == OutputMode.PRE, "Config JSON roundtrip preserves PRE");
    Assert(reloaded.Language == AppLanguage.En, "Config JSON roundtrip preserves Language=En");
    Assert(reloaded.CustomLcd.Enabled, "Config JSON roundtrip preserves CustomLcd.Enabled");
    Assert(reloaded.CustomLcd.Text == "CHECNEP", "Config JSON roundtrip preserves CustomLcd.Text");
    Assert(reloaded.Hotkeys.ToggleMute.ToString() == "Alt+M", "Config JSON roundtrip preserves Hotkeys.ToggleMute");
    Assert(reloaded.Rgb.State == RgbStateMode.Custom && reloaded.Rgb.Effect == RgbEffectMode.Chase,
        "Config JSON roundtrip preserves Rgb State=Custom and Effect=Chase");

    File.WriteAllText(cfgPath, """{"Language": 99}""");
    var langCoerced = TrayConfig.Load(cfgPath);
    Assert(langCoerced.Language == AppLanguage.Ru, "Invalid Language value coerces safely to Ru");

    Assert(AutostartManager.FormatRunCommand(@"E:\Code\dist\K11Control.exe") == "\"E:\\Code\\dist\\K11Control.exe\"",
        "AutostartManager quotes valid .exe path cleanly");
    Assert(AutostartManager.FormatRunCommand(@"E:\Code\dotnet.dll") is null,
        "AutostartManager rejects non-.exe host paths");

    // Verify legacy V1 config migrates LCD, RGB (Enabled+Mode), and old Ctrl+Alt hotkeys to Alt+1..Alt+5
    File.WriteAllText(cfgPath, """
    {
      "Hotkeys": {
        "ToggleOutput": { "Modifiers": "Control, Alt", "Key": "O" },
        "ToggleCustomText": { "Modifiers": "Control, Alt", "Key": "T" },
        "ToggleRgb": { "Modifiers": "Control, Alt", "Key": "L" }
      },
      "CustomLcd": { "Enabled": true, "TopLinePo": "chec", "TopLinePre": "SPKt", "BottomText": "nep" },
      "Rgb": { "Enabled": true, "Mode": "Rainbow", "Brightness": 75 }
    }
    """);
    var migrated = TrayConfig.Load(cfgPath);
    Assert(migrated.CustomLcd.Text == "CHECNEP", $"Legacy V1 LCD migrates cleanly to 'CHECNEP' (got '{migrated.CustomLcd.Text}')");
    Assert(migrated.Rgb.State == RgbStateMode.Custom && migrated.Rgb.Effect == RgbEffectMode.Rainbow,
        "Legacy V1 Rgb Enabled=true/Rainbow migrates to State=Custom, Effect=Rainbow");
    Assert(migrated.Hotkeys.ToggleOutput.ToString() == "Alt+1"
        && migrated.Hotkeys.SwitchToPre.ToString() == "Alt+2"
        && migrated.Hotkeys.SwitchToPo.ToString() == "Alt+3"
        && migrated.Hotkeys.ToggleCustomText.ToString() == "Alt+4"
        && migrated.Hotkeys.ToggleRgb.ToString() == "Alt+5",
        "Legacy V1 default Ctrl+Alt hotkeys migrate cleanly to Alt+1..Alt+5");

    File.WriteAllText(cfgPath, """{"Rgb": {"Enabled": false, "Mode": "Rainbow"}}""");
    var migOff = TrayConfig.Load(cfgPath);
    Assert(migOff.Rgb.State == RgbStateMode.Off, "Legacy V1 Rgb Enabled=false migrates to State=Off");

    File.WriteAllText(cfgPath, """{"Rgb": {"Enabled": true, "Mode": "Stock"}}""");
    var migStock = TrayConfig.Load(cfgPath);
    Assert(migStock.Rgb.State == RgbStateMode.On, "Legacy V1 Rgb Stock migrates to State=On");

    // Inject unsupported LO numeric value (1) into JSON and verify Load coerces to PO
    File.WriteAllText(cfgPath, """{"PreferredOutput": 1}""");
    var coerced = TrayConfig.Load(cfgPath);
    Assert(coerced.PreferredOutput == OutputMode.PO, "Corrupt/LO output value coerced safely to PO");
}
finally
{
    Directory.Delete(tempDir, recursive: true);
}

Assert(new HotkeyBinding().ToString() == "(None)", "Unset hotkey formats as (None)");

// 5. Passive Stage 0 status check runs safely regardless of DAC connection state
string statusReport = Stage0ProbeRunner.BuildStatusReport();
Assert(statusReport.Contains("FiiO K11 R2R Stage 0 Passive Status"), "Passive Stage 0 report executes cleanly");

// 6. Stage 2 UAC2 zero-click escape protocol framing, toggle uniqueness & round-trip decoding
byte[] outPkt = DacCommandProtocol.BuildSetOutputModePacket(OutputMode.PRE);
Assert(outPkt.Length == 3 && outPkt[0] == 0x01 && outPkt[1] == 2 && (outPkt[0] ^ outPkt[1] ^ outPkt[2]) == DacCommandProtocol.ChecksumSeed,
    "SetOutputMode(PRE) packet builds valid 3-byte frame with 0xA5 XOR checksum");

byte[] togglePkt = DacCommandProtocol.BuildSetOutputActionPacket(OutputAction.Toggle);
Assert(togglePkt.Length == 3 && togglePkt[0] == 0x01 && togglePkt[1] == 0xFF && (togglePkt[0] ^ togglePkt[1] ^ togglePkt[2]) == DacCommandProtocol.ChecksumSeed,
    "BuildSetOutputActionPacket(Toggle) packet builds valid 3-byte hardware toggle frame (0x01, 0xFF, 0x5B)");

byte[] gainLowPkt = DacCommandProtocol.BuildSetGainActionPacket(GainAction.Low);
byte[] gainMidPkt = DacCommandProtocol.BuildSetGainActionPacket(GainAction.Mid);
byte[] gainHighPkt = DacCommandProtocol.BuildSetGainActionPacket(GainAction.High);
byte[] gainCyclePkt = DacCommandProtocol.BuildSetGainActionPacket(GainAction.Cycle);
Assert(gainLowPkt.Length == 3 && gainLowPkt[0] == 0x05 && gainLowPkt[1] == 0 && (gainLowPkt[0] ^ gainLowPkt[1] ^ gainLowPkt[2]) == DacCommandProtocol.ChecksumSeed,
    "BuildSetGainActionPacket(Low) builds valid 0x05 packet");
Assert(gainMidPkt.Length == 3 && gainMidPkt[0] == 0x05 && gainMidPkt[1] == 1 && (gainMidPkt[0] ^ gainMidPkt[1] ^ gainMidPkt[2]) == DacCommandProtocol.ChecksumSeed,
    "BuildSetGainActionPacket(Mid) builds valid 0x05 packet");
Assert(gainHighPkt.Length == 3 && gainHighPkt[0] == 0x05 && gainHighPkt[1] == 2 && (gainHighPkt[0] ^ gainHighPkt[1] ^ gainHighPkt[2]) == DacCommandProtocol.ChecksumSeed,
    "BuildSetGainActionPacket(High) builds valid 0x05 packet");
Assert(gainCyclePkt.Length == 3 && gainCyclePkt[0] == 0x05 && gainCyclePkt[1] == 0xFF && (gainCyclePkt[0] ^ gainCyclePkt[1] ^ gainCyclePkt[2]) == DacCommandProtocol.ChecksumSeed,
    "BuildSetGainActionPacket(Cycle) builds valid 0x05 0xFF packet");

byte[] filterNosPkt = DacCommandProtocol.BuildSetFilterActionPacket(FilterAction.Nos);
byte[] filterOsPkt = DacCommandProtocol.BuildSetFilterActionPacket(FilterAction.Os);
byte[] filterTogglePkt = DacCommandProtocol.BuildSetFilterActionPacket(FilterAction.Toggle);
Assert(filterNosPkt.Length == 3 && filterNosPkt[0] == 0x06 && filterNosPkt[1] == 0 && (filterNosPkt[0] ^ filterNosPkt[1] ^ filterNosPkt[2]) == DacCommandProtocol.ChecksumSeed,
    "BuildSetFilterActionPacket(Nos) builds valid 0x06 packet");
Assert(filterOsPkt.Length == 3 && filterOsPkt[0] == 0x06 && filterOsPkt[1] == 1 && (filterOsPkt[0] ^ filterOsPkt[1] ^ filterOsPkt[2]) == DacCommandProtocol.ChecksumSeed,
    "BuildSetFilterActionPacket(Os) builds valid 0x06 packet");
Assert(filterTogglePkt.Length == 3 && filterTogglePkt[0] == 0x06 && filterTogglePkt[1] == 0xFF && (filterTogglePkt[0] ^ filterTogglePkt[1] ^ filterTogglePkt[2]) == DacCommandProtocol.ChecksumSeed,
    "BuildSetFilterActionPacket(Toggle) builds valid 0x06 0xFF packet");

var lcdCfg = new CustomLcdConfig { Enabled = true, Text = "SPK R2R", ActivityRevertDelayMs = 2500 };
byte[] lcdPkt = DacCommandProtocol.BuildSetCustomLcdPacket(lcdCfg);
Assert(lcdPkt.Length == 11 && lcdPkt[0] == 0x02 && lcdPkt[1] == 1 && lcdPkt[2] == 25,
    "SetCustomLcd packet builds 11-byte frame with 2500ms -> 25ds delay");
Assert(System.Text.Encoding.ASCII.GetString(lcdPkt, 3, 7) == "SPK R2R",
    $"SetCustomLcd payload embeds sanitized 7-char text: {System.Text.Encoding.ASCII.GetString(lcdPkt, 3, 7)}");

byte[] rgbOffPkt = DacCommandProtocol.BuildSetRgbPacket(new RgbConfig { State = RgbStateMode.Off, Effect = RgbEffectMode.Rainbow, Brightness = 90 });
Assert(rgbOffPkt.Length == 13 && rgbOffPkt[0] == 0x03 && rgbOffPkt[1] == (byte)RgbEffectMode.Solid && rgbOffPkt[2] == 0,
    "SetRgb(State=Off) encodes Solid mode with 0 intensity");

byte[] rgbStockPkt = DacCommandProtocol.BuildSetRgbPacket(new RgbConfig { State = RgbStateMode.On, Effect = RgbEffectMode.Rainbow, Brightness = 90 });
Assert(rgbStockPkt.Length == 13 && rgbStockPkt[0] == 0x03 && rgbStockPkt[1] == 0,
    "SetRgb(State=On) encodes Stock firmware mode (0)");

var rgbCustomCfg = new RgbConfig { State = RgbStateMode.Custom, Effect = RgbEffectMode.Rainbow, Brightness = 100, ZoneHexColors = ["#FF0011", "#22AA33", "#4455FF"] };
byte[] rgbCustomPkt = DacCommandProtocol.BuildSetRgbPacket(rgbCustomCfg);
Assert(rgbCustomPkt.Length == 13 && rgbCustomPkt[1] == (byte)RgbEffectMode.Rainbow && rgbCustomPkt[2] == 255
    && rgbCustomPkt[3] == 0x44 && rgbCustomPkt[4] == 0x55 && rgbCustomPkt[5] == 0xFF
    && rgbCustomPkt[6] == 0x22 && rgbCustomPkt[7] == 0xAA && rgbCustomPkt[8] == 0x33
    && rgbCustomPkt[9] == 0xFF && rgbCustomPkt[10] == 0x00 && rgbCustomPkt[11] == 0x11,
    "SetRgb(Custom) maps UI zones [Left, Center, Right] to physical WS2812 chain [LED2, LED1, LED0]");

byte[] zeroStressPkt = DacCommandProtocol.BuildCommandPacket([0x03, 0x00, 0x00, 0x00, 0x00, 0x00]);
foreach (var (ch0Init, ch1Init) in new (float, float)[] { (-20f, -20f), (0f, 0f), (-62f, -63f), (-60f, -60f), (-10f, -30f) })
{
    foreach (byte[] pkt in new[] { outPkt, togglePkt, gainLowPkt, gainHighPkt, gainCyclePkt, filterNosPkt, filterOsPkt, filterTogglePkt, lcdPkt, rgbCustomPkt, DacCommandProtocol.BuildRebootPacket(), zeroStressPkt })
    {
        IReadOnlyList<VolumeStep> steps = DacCommandProtocol.EncodeUac2Transfer(pkt, ch0Init, ch1Init, interWriteMs: 0);

        float prev0 = ch0Init;
        float prev1 = ch1Init;
        bool committed = false;
        bool consecutiveOk = true;
        foreach (VolumeStep s in steps)
        {
            if (s.Channel == 0)
            {
                if (!committed && MathF.Abs(s.LevelDb - prev0) < 0.5f)
                    consecutiveOk = false;
                prev0 = s.LevelDb;
                if (MathF.Abs(s.LevelDb - (-60f)) < 0.5f)
                    committed = true;
            }
            else
            {
                if (!committed && MathF.Abs(s.LevelDb - prev1) < 0.5f)
                    consecutiveOk = false;
                prev1 = s.LevelDb;
            }
        }
        Assert(consecutiveOk, $"Pre-commit UAC2 channel writes strictly change levelDb on init=({ch0Init},{ch1Init}) cmd=0x{pkt[0]:X2}");

        bool decodedOk = DacCommandProtocol.TryDecodeReferenceStream(steps, out byte[]? decoded);
        Assert(decodedOk && decoded is not null && decoded.SequenceEqual(pkt),
            $"UAC2 transfer round-trips exactly for cmd=0x{pkt[0]:X2} from init=({ch0Init},{ch1Init})");

        Assert(MathF.Abs(prev0 - ch0Init) < 0.5f && MathF.Abs(prev1 - ch1Init) < 0.5f,
            $"Final channel levels restore to ({ch0Init},{ch1Init}) after commit (got {prev0},{prev1})");
    }
}

byte[] badChkPkt = (byte[])outPkt.Clone();
badChkPkt[^1] ^= 0xFF;
Assert(!DacCommandProtocol.TryDecodeReferenceStream(DacCommandProtocol.EncodeUac2Transfer(badChkPkt, -20f, -20f, 0), out _),
    "Reference decoder rejects packet with corrupted XOR checksum");

// 7. HideTopIcons flag encoding & Safe Connect/Settings SyncScope rules
var lcdHideBoth = new CustomLcdConfig { Enabled = true, HideTopIcons = true };
byte[] lcdHideBothPkt = DacCommandProtocol.BuildSetCustomLcdPacket(lcdHideBoth);
Assert(lcdHideBothPkt[1] == 0x03, $"HideTopIcons + Enabled sets flags=0x03 (got 0x{lcdHideBothPkt[1]:X2})");

var defaultCfg = new TrayConfig();
Assert(TrayApplicationContext.ComputeConnectSyncScope(defaultCfg) == SyncScope.None,
    "Default connect scope is None so PO/PRE hardware output and stock RGB/LCD are untouched on startup");

var rgbOffCfg = new TrayConfig { Rgb = new RgbConfig { State = RgbStateMode.Off } };
Assert(!TrayApplicationContext.ComputeConnectSyncScope(rgbOffCfg).HasFlag(SyncScope.OutputMode),
    "Connect sync with EnforceOutputOnConnect=false never forces OutputMode");
Assert(TrayApplicationContext.ComputeConnectSyncScope(rgbOffCfg) == SyncScope.Rgb,
    "Connect sync applies RGB Off state when selected");

var enforceOnCfg = new TrayConfig { EnforceOutputOnConnect = true, PreferredOutput = OutputMode.PRE };
Assert(TrayApplicationContext.ComputeConnectSyncScope(enforceOnCfg).HasFlag(SyncScope.OutputMode),
    "Connect sync forces OutputMode only when EnforceOutputOnConnect is enabled");

var prevCfg = new TrayConfig();
var editedRgbCfg = new TrayConfig { Rgb = new RgbConfig { State = RgbStateMode.Custom, Effect = RgbEffectMode.Rainbow, Brightness = 60 } };
SyncScope applyRgbScope = TrayApplicationContext.ComputeSettingsApplyScope(prevCfg, editedRgbCfg);
Assert(applyRgbScope == SyncScope.Rgb && !applyRgbScope.HasFlag(SyncScope.OutputMode),
    "Applying RGB changes in Settings never sends SetOutputMode");

var prevWithLcdCfg = new TrayConfig { CustomLcd = new CustomLcdConfig { Enabled = true, Text = "PRIVET" } };
var editedRgbWithLcdCfg = new TrayConfig
{
    CustomLcd = new CustomLcdConfig { Enabled = true, Text = "PRIVET" },
    Rgb = new RgbConfig { State = RgbStateMode.Custom, Effect = RgbEffectMode.Solid, Brightness = 47, ZoneHexColors = ["#58FF5A", "#58FF5A", "#58FF5A"] }
};
SyncScope applyWithLcdScope = TrayApplicationContext.ComputeSettingsApplyScope(prevWithLcdCfg, editedRgbWithLcdCfg);
Assert(applyWithLcdScope == (SyncScope.Rgb | SyncScope.CustomLcd) && !applyWithLcdScope.HasFlag(SyncScope.OutputMode),
    "Applying RGB changes with CustomLcd enabled also re-syncs CustomLcd to keep DAC RAM intact");

var editedOutCfg = new TrayConfig { EnforceOutputOnConnect = true, PreferredOutput = OutputMode.PRE };
Assert(TrayApplicationContext.ComputeSettingsApplyScope(prevCfg, editedOutCfg) == SyncScope.None,
    "Changing startup-only output settings in Settings never immediately switches live DAC output");

// 8. Variant B direct 14-segment masks (0x07): 16-byte packing, round-trip decode, UAC2 transport & JSON Mode/SegmentMasks
var segCfg = new CustomLcdConfig
{
    Mode = CustomLcdMode.Segments,
    HideTopIcons = true,
    ActivityRevertDelayMs = 4200,
    SegmentMasks = [0x3FFF, 0x0001, 0x2000, 0x1555, 0x2AAA, 0x0410, 0x3C87]
};
byte[] segPkt = DacCommandProtocol.BuildEffectiveLcdPacket(segCfg);
Assert(segPkt.Length == DacCommandProtocol.MaxPacketBytes && segPkt[0] == DacCommandProtocol.CmdSetCustomLcdMasks,
    $"Direct 14-segment mask packet fits exact 16-byte UAC2 frame with opcode 0x07 (len={segPkt.Length})");
Assert(DacCommandProtocol.TryDecodeLcdMasksPacket(segPkt, out bool decEn, out bool decHide, out byte decDs, out ushort[]? decMasks)
    && decEn && decHide && decDs == 42 && decMasks is not null && decMasks.SequenceEqual(segCfg.SegmentMasks),
    "All 7 x 14-bit masks, enable/hideTopIcons bits, revert_ds=42 and XOR checksum round-trip losslessly");

var cyrCfg = new CustomLcdConfig { Mode = CustomLcdMode.Text, Text = "ПРИВЕТ!" };
byte[] cyrPkt = DacCommandProtocol.BuildEffectiveLcdPacket(cyrCfg);
Assert(cyrPkt.Length == 16 && cyrPkt[0] == DacCommandProtocol.CmdSetCustomLcdMasks
    && DacCommandProtocol.TryDecodeLcdMasksPacket(cyrPkt, out bool cyrEn, out _, out _, out ushort[]? cyrMasks)
    && cyrEn && cyrMasks is not null && cyrMasks.Take(6).All(m => m != 0),
    "Cyrillic text auto-dispatches via 16-byte cmd 0x07 with non-zero glyph masks");

byte[] legacyPkt = DacCommandProtocol.BuildEffectiveLcdPacket(new CustomLcdConfig { Mode = CustomLcdMode.Text, Text = "K11 R2R" });
Assert(legacyPkt.Length == 11 && legacyPkt[0] == DacCommandProtocol.CmdSetCustomLcd,
    "Pure stock-ASCII text keeps 11-byte cmd 0x02 for backward compatibility with unpatched FW");

byte[] turnOffMasksPkt = DacCommandProtocol.BuildEffectiveLcdPacket(new CustomLcdConfig { Mode = CustomLcdMode.Normal });
Assert(turnOffMasksPkt.Length == 11 && turnOffMasksPkt[0] == DacCommandProtocol.CmdSetCustomLcd && (turnOffMasksPkt[1] & 1) == 0,
    "Normal LCD mode clears overlay via cmd 0x02 enable=0");

var normalHideIconsCfg = new CustomLcdConfig { Mode = CustomLcdMode.Normal, HideTopIconsNormal = true, HideTopIconsText = false, HideTopIconsSegments = false, ScreenOff = false };
byte[] normalHideIconsPkt = DacCommandProtocol.BuildEffectiveLcdPacket(normalHideIconsCfg);
Assert(normalHideIconsPkt.Length == 11 && normalHideIconsPkt[0] == DacCommandProtocol.CmdSetCustomLcd
    && normalHideIconsPkt[1] == 0x02
    && TrayApplicationContext.ComputeConnectSyncScope(new TrayConfig { CustomLcd = normalHideIconsCfg }).HasFlag(SyncScope.CustomLcd),
    "Normal + HideTopIconsNormal sends cmd 0x02 flags=0x02 (enable=0, hideIcons=1) and syncs on connect");

var perModeIconsCfg = new CustomLcdConfig
{
    Mode = CustomLcdMode.Text,
    HideTopIconsNormal = true,
    HideTopIconsText = false,
    HideTopIconsSegments = true
};
Assert(DacCommandProtocol.BuildEffectiveLcdPacket(perModeIconsCfg)[1] == 0x01
    && !perModeIconsCfg.EffectiveHideTopIcons
    && perModeIconsCfg.GetHideTopIcons(CustomLcdMode.Normal)
    && !perModeIconsCfg.GetHideTopIcons(CustomLcdMode.Text)
    && perModeIconsCfg.GetHideTopIcons(CustomLcdMode.Segments),
    "HideTopIcons is tracked independently per mode (Normal / Text / Segments)");

var normalScreenOffCfg = new CustomLcdConfig { Mode = CustomLcdMode.Normal, ScreenOff = true, HideTopIconsNormal = false, Text = "PRIVET" };
byte[] normalScreenOffPkt = DacCommandProtocol.BuildEffectiveLcdPacket(normalScreenOffCfg);
Assert(normalScreenOffPkt.Length == 11 && normalScreenOffPkt[0] == DacCommandProtocol.CmdSetCustomLcd
    && normalScreenOffPkt[1] == 0x05
    && normalScreenOffPkt.Skip(3).Take(7).All(b => b == (byte)' ')
    && TrayApplicationContext.ComputeConnectSyncScope(new TrayConfig { CustomLcd = normalScreenOffCfg }).HasFlag(SyncScope.CustomLcd),
    "Normal + ScreenOff + HideTopIconsNormal=false sends cmd 0x02 flags=0x05 (enable|screenOff) with 7 spaces");

var normalScreenOffHideCfg = new CustomLcdConfig { Mode = CustomLcdMode.Normal, ScreenOff = true, HideTopIconsNormal = true, Text = "PRIVET" };
byte[] normalScreenOffHidePkt = DacCommandProtocol.BuildEffectiveLcdPacket(normalScreenOffHideCfg);
Assert(normalScreenOffHidePkt.Length == 11 && normalScreenOffHidePkt[0] == DacCommandProtocol.CmdSetCustomLcd
    && normalScreenOffHidePkt[1] == 0x07
    && normalScreenOffHidePkt.Skip(3).Take(7).All(b => b == (byte)' '),
    "Normal + ScreenOff + HideTopIconsNormal=true sends cmd 0x02 flags=0x07 (enable|hideIcons|screenOff) with 7 spaces");

var prevNorm = new TrayConfig { CustomLcd = new CustomLcdConfig { Mode = CustomLcdMode.Normal, ScreenOff = false } };
var nextNorm = new TrayConfig { CustomLcd = new CustomLcdConfig { Mode = CustomLcdMode.Normal, ScreenOff = true } };
Assert(TrayApplicationContext.ComputeSettingsApplyScope(prevNorm, nextNorm) == SyncScope.CustomLcd,
    "ComputeSettingsApplyScope detects ScreenOff toggle in Normal mode cleanly");

var prevScreenOffIcons = new TrayConfig { CustomLcd = new CustomLcdConfig { Mode = CustomLcdMode.Normal, ScreenOff = true, HideTopIconsNormal = false } };
var nextScreenOffIcons = new TrayConfig { CustomLcd = new CustomLcdConfig { Mode = CustomLcdMode.Normal, ScreenOff = true, HideTopIconsNormal = true } };
Assert(TrayApplicationContext.ComputeSettingsApplyScope(prevScreenOffIcons, nextScreenOffIcons) == SyncScope.CustomLcd,
    "ComputeSettingsApplyScope detects HideTopIconsNormal change while ScreenOff is enabled");

var prevHiddenOther = new TrayConfig { CustomLcd = new CustomLcdConfig { Mode = CustomLcdMode.Normal, HideTopIconsText = false } };
var nextHiddenOther = new TrayConfig { CustomLcd = new CustomLcdConfig { Mode = CustomLcdMode.Normal, HideTopIconsText = true } };
Assert(TrayApplicationContext.ComputeSettingsApplyScope(prevHiddenOther, nextHiddenOther) == SyncScope.None,
    "Changing inactive mode's HideTopIcons checkbox does not trigger redundant DAC RAM write");

IReadOnlyList<VolumeStep> segUacSteps = DacCommandProtocol.EncodeUac2Transfer(segPkt, -18f, -24f, interWriteMs: 0);
Assert(DacCommandProtocol.TryDecodeReferenceStream(segUacSteps, out byte[]? segDecodedUac)
    && segDecodedUac is not null && segDecodedUac.SequenceEqual(segPkt),
    "16-byte cmd 0x07 packet round-trips losslessly through UAC2 Channel Volume encoder/decoder");

string segJsonDir = Path.Combine(Path.GetTempPath(), $"k11_seg_test_{Guid.NewGuid():N}");
Directory.CreateDirectory(segJsonDir);
try
{
    string segCfgPath = Path.Combine(segJsonDir, "config.json");
    var saveSeg = new TrayConfig { CustomLcd = segCfg };
    saveSeg.CustomLcd.Mode = CustomLcdMode.Normal; // verify LastCustomMode remembers Segments across Normal toggle!
    saveSeg.Save(segCfgPath);
    var loadedSeg = TrayConfig.Load(segCfgPath);
    Assert(loadedSeg.CustomLcd.Mode == CustomLcdMode.Normal
        && loadedSeg.CustomLcd.LastCustomMode == CustomLcdMode.Segments
        && loadedSeg.CustomLcd.SegmentMasks.SequenceEqual(segCfg.SegmentMasks),
        "Config JSON roundtrip preserves SegmentMasks and remembers LastCustomMode=Segments across Normal toggle");

    var prevSeg = new TrayConfig { CustomLcd = new CustomLcdConfig { Mode = CustomLcdMode.Segments, SegmentMasks = [1, 0, 0, 0, 0, 0, 0] } };
    var nextSeg = new TrayConfig { CustomLcd = new CustomLcdConfig { Mode = CustomLcdMode.Segments, SegmentMasks = [3, 0, 0, 0, 0, 0, 0] } };
    Assert(TrayApplicationContext.ComputeSettingsApplyScope(prevSeg, nextSeg) == SyncScope.CustomLcd,
        "ComputeSettingsApplyScope detects individual SegmentMasks edits cleanly");

    // 9. Multi-frame animation templates, 0x08 framing, UAC2 round-trip & preset persistence
    foreach (SticksTemplateKind kind in SticksTemplateEngine.OrderedTemplates)
    {
        if (kind == SticksTemplateKind.Custom)
            continue;
        var tpl = SticksPreset.FromTemplate($"Test_{kind}", kind, SticksTemplateEngine.SupportsInnerText(kind) ? "ЗВУК" : null);
        Assert(tpl.FrameCount == 4 && tpl.Frames.Take(4).All(fr => fr.Any(m => m != 0)),
            $"Template {kind} builds 4 non-empty 14-seg frames (step={tpl.StepIntervalMs}ms)");
    }

    var reelsPreset = SticksPreset.FromTemplate("Reels ZVUK", SticksTemplateKind.ReelsText, "ЗВУК");
    var animLcd = new CustomLcdConfig
    {
        Mode = CustomLcdMode.Segments,
        HideTopIcons = true,
        ActivityRevertDelayMs = 3100,
        SticksPresets = [reelsPreset],
        ActiveSticksPresetIndex = 0
    };
    animLcd.ApplyPresetToLive(reelsPreset);
    IReadOnlyList<byte[]> animPkts = DacCommandProtocol.BuildEffectiveLcdPackets(animLcd);
    Assert(animPkts.Count == 4 && animPkts.All(p => p.Length == 16 && p[0] == DacCommandProtocol.CmdSetCustomLcdAnimFrame),
        "4-frame Segments preset emits four 16-byte cmd 0x08 packets");

    for (int f = 0; f < animPkts.Count; f++)
    {
        bool okDec = DacCommandProtocol.TryDecodeLcdAnimFramePacket(
            animPkts[f], out bool fEn, out bool fHide, out int fIdx, out int fTot, out byte p14, out ushort[]? fMasks);
        byte expectedP14 = f == 0 ? (byte)31 : (byte)25;
        Assert(okDec && fEn && fHide && fIdx == f && fTot == 4 && p14 == expectedP14
            && fMasks is not null && fMasks.SequenceEqual(reelsPreset.Frames[f]),
            $"Frame {f} cmd 0x08 decodes exact masks, flags, param14={expectedP14}, frameIndex={f}/4");

        IReadOnlyList<VolumeStep> fSteps = DacCommandProtocol.EncodeUac2Transfer(animPkts[f], -12f, -20f, interWriteMs: 0);
        Assert(DacCommandProtocol.TryDecodeReferenceStream(fSteps, out byte[]? fUac)
            && fUac is not null && fUac.SequenceEqual(animPkts[f]),
            $"Anim frame {f} cmd 0x08 round-trips cleanly through UAC2 stream");
    }

    var animSave = new TrayConfig { Language = AppLanguage.Ru, CustomLcd = animLcd };
    animSave.CustomLcd.EnsureSticksPresetsInitialized(AppLanguage.Ru);
    string animCfgPath = Path.Combine(segJsonDir, "anim_config.json");
    animSave.Save(animCfgPath);
    var animReloaded = TrayConfig.Load(animCfgPath);
    Assert(animReloaded.CustomLcd.FrameCount == 4
        && animReloaded.CustomLcd.SticksPresets.Count >= 1
        && animReloaded.CustomLcd.SticksPresets[0].InnerText == "ЗВУК"
        && animReloaded.CustomLcd.ResolveFrameMasks(2).SequenceEqual(reelsPreset.Frames[2]),
        "Config JSON preserves animated SticksPresets, FrameCount, StepIntervalMs, and InnerText");

    var seededCfg = new CustomLcdConfig { SegmentMasks = [0x0123, 0, 0, 0, 0, 0, 0] };
    seededCfg.EnsureSticksPresetsInitialized(AppLanguage.Ru);
    Assert(seededCfg.SticksPresets.Count == 8
        && !seededCfg.SticksPresets[0].IsBuiltIn
        && seededCfg.SticksPresets[0].Frames[0][0] == 0x0123
        && seededCfg.SticksPresets[1].IsBuiltIn
        && seededCfg.SticksPresets[1].Template == SticksTemplateKind.ReelsText,
        "EnsureSticksPresetsInitialized preserves custom preset 0 (IsBuiltIn=false) and marks 7 starter presets IsBuiltIn=true");

    // Pre-IsBuiltIn starter JSON presets promote cleanly and ignore tampered frame masks while keeping InnerText/StepIntervalMs
    var legacyStarter = new SticksPreset
    {
        Name = seededCfg.SticksPresets[1].Name,
        IsBuiltIn = false,
        Template = SticksTemplateKind.ReelsText,
        InnerText = "ЗВУК",
        FrameCount = 1,
        StepIntervalMs = 400,
        Frames = SticksPreset.CreateEmptyFrames()
    };
    var upgradedCfg = new CustomLcdConfig { SticksPresets = [legacyStarter] };
    upgradedCfg.EnsureSticksPresetsInitialized(AppLanguage.Ru);
    var expectedReels = SticksTemplateEngine.Build(SticksTemplateKind.ReelsText, "ЗВУК").Frames;
    Assert(upgradedCfg.SticksPresets[0].IsBuiltIn
        && upgradedCfg.SticksPresets[0].FrameCount == 4
        && upgradedCfg.SticksPresets[0].StepIntervalMs == 400
        && upgradedCfg.SticksPresets[0].InnerText == "ЗВУК"
        && upgradedCfg.SticksPresets[0].Frames[0].SequenceEqual(expectedReels[0]),
        "Built-in starter migration promotes IsBuiltIn=true, keeps InnerText/StepIntervalMs, and restores template geometry");

    // Partial starter list (e.g. deleted ReelsText in an earlier build) restores missing built-ins without shifting user active selection
    var customEntry = new SticksPreset { Name = "Custom 1", Template = SticksTemplateKind.Custom, FrameCount = 2 };
    var bracketsBuiltIn = SticksPreset.FromTemplate(seededCfg.SticksPresets[2].Name, SticksTemplateKind.BracketsText, isBuiltIn: true);
    var partialCfg = new CustomLcdConfig
    {
        SticksPresets = [customEntry, bracketsBuiltIn],
        ActiveSticksPresetIndex = 1
    };
    partialCfg.EnsureSticksPresetsInitialized(AppLanguage.Ru);
    Assert(partialCfg.SticksPresets.Count == 8
        && !partialCfg.SticksPresets[0].IsBuiltIn
        && partialCfg.SticksPresets[1].IsBuiltIn && partialCfg.SticksPresets[1].Template == SticksTemplateKind.ReelsText
        && partialCfg.SticksPresets[2] == bracketsBuiltIn
        && partialCfg.ActiveSticksPresetIndex == 2,
        "EnsureSticksPresetsInitialized restores missing built-in presets in canonical order and preserves active selection");
}
finally
{
    Directory.Delete(segJsonDir, recursive: true);
}

Assert(!defaultCfg.Hotkeys.CycleGain.IsConfigured && !defaultCfg.Hotkeys.ToggleFilter.IsConfigured,
    "New GainCycle and FilterToggle hotkeys default to unbound to avoid shortcut conflicts");

var sameCustomRgb = new TrayConfig
{
    Rgb = new RgbConfig { State = RgbStateMode.Custom, Effect = RgbEffectMode.Solid, Brightness = 61, ZoneHexColors = ["#F59E0B", "#F59E0B", "#F59E0B"] },
    CustomLcd = new CustomLcdConfig { Mode = CustomLcdMode.Text, Text = "R2R" }
};
Assert(TrayApplicationContext.ComputeSettingsApplyScope(sameCustomRgb, sameCustomRgb) == SyncScope.None
    && TrayApplicationContext.ComputeExplicitSettingsApplyScope(sameCustomRgb, sameCustomRgb) == (SyncScope.Rgb | SyncScope.CustomLcd)
    && !TrayApplicationContext.ComputeExplicitSettingsApplyScope(sameCustomRgb, sameCustomRgb).HasFlag(SyncScope.OutputMode),
    "ComputeExplicitSettingsApplyScope re-syncs unchanged active RGB/LCD RAM state without altering output mode");

Assert(TrayApplicationContext.ComputeExplicitSettingsApplyScope(new TrayConfig(), new TrayConfig()) == SyncScope.None,
    "ComputeExplicitSettingsApplyScope stays None when RGB=On and LCD=Normal");

int hkChangedCount = 0;
using (var hkBox = new K11Control.UI.HotkeyCaptureBox())
{
    hkBox.BindingChanged += (_, _) => hkChangedCount++;
    hkBox.Binding = new HotkeyBinding { Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift, Key = Keys.F10 };
    hkBox.Binding = new HotkeyBinding { Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift, Key = Keys.F10 };
    hkBox.Binding = new HotkeyBinding();
}
Assert(hkChangedCount == 2, $"HotkeyCaptureBox fires BindingChanged on real edits only (got {hkChangedCount})");

using (var form = new K11Control.UI.SettingsForm(
    new TrayConfig(), false, false, _ => { }, _ => { }, () => { }, _ => { }, () => { }))
{
    form.CreateControl();
    var selectPage = typeof(K11Control.UI.SettingsForm).GetMethod("SelectPage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    var hkField = typeof(K11Control.UI.SettingsForm).GetField("_hkToggleOut", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    selectPage.Invoke(form, [3]);
    var box = (K11Control.UI.HotkeyCaptureBox)hkField.GetValue(form)!;
    form.ActiveControl = box;
    Assert(form.ActiveControl == box, "SettingsForm focuses HotkeyCaptureBox on Hotkeys page");
    selectPage.Invoke(form, [0]);
    Assert(form.ActiveControl is null && !box.Visible,
        "Switching away from Hotkeys page clears focus from HotkeyCaptureBox cleanly");
}

// Channel balance detection & clamping pure helpers
Assert(!CoreAudioVolumeService.IsChannelImbalanced(0f, 0f),
    "IsChannelImbalanced returns false when both channels are 0 dB");
Assert(!CoreAudioVolumeService.IsChannelImbalanced(-0.2f, 0f),
    "IsChannelImbalanced ignores minor sub-0.5 dB rounding noise");
Assert(CoreAudioVolumeService.IsChannelImbalanced(-1f, 0f)
    && CoreAudioVolumeService.IsChannelImbalanced(0f, -0.5f),
    "IsChannelImbalanced detects L/R channel skew >= 0.5 dB");
Assert(!CoreAudioVolumeService.IsChannelImbalanced(float.NaN, 0f),
    "IsChannelImbalanced safely rejects non-finite channel values");
Assert(CoreAudioVolumeService.ComputeTargetBalancedDb(-1f, 0f, -63f, 0f) == 0f
    && CoreAudioVolumeService.ComputeTargetBalancedDb(-12f, -15f, -63f, 0f) == -12f
    && CoreAudioVolumeService.ComputeTargetBalancedDb(2f, -1f, -63f, 0f) == 0f,
    "ComputeTargetBalancedDb aligns to highest channel within device dB bounds");

// Conditional L/R balance alert & Apply button feedback on SettingsForm
K11AudioEndpointInfo balancedEp = new("k11-id", "FIIO K11 R2R", "FIIO", 0f, 1f, 0f, 0f, false, -63f, 0f, 0.5f);
K11AudioEndpointInfo skewedEp = new("k11-id", "FIIO K11 R2R", "FIIO", -0.5f, 0.94f, -1f, 0f, false, -63f, 0f, 0.5f);
K11AudioEndpointInfo nextFixResult = balancedEp;
bool nextApplyResult = true;
int fixCalls = 0;
int muteCalls = 0;

using (var form = new K11Control.UI.SettingsForm(
    new TrayConfig { Language = AppLanguage.Ru },
    dacConnected: false,
    audioMuted: false,
    _ => { },
    _ => { },
    () => { },
    _ => { },
    () => muteCalls++,
    onApplyWithFeedback: (_, cb) => cb(nextApplyResult),
    onFixBalance: () => { fixCalls++; return nextFixResult; },
    initialAudioEndpoint: skewedEp))
{
    form.CreateControl();
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var balanceVisibleField = typeof(K11Control.UI.SettingsForm).GetField("_balanceBannerVisible", flags)!;
    bool IsBannerShown() => (bool)balanceVisibleField.GetValue(form)!;
    var balanceCard = (Control)typeof(K11Control.UI.SettingsForm).GetField("_balanceCard", flags)!.GetValue(form)!;
    var topTextProp = balanceCard.GetType().GetProperty("TopText")!;
    var bottomTextProp = balanceCard.GetType().GetProperty("BottomText")!;
    string BalanceTop() => (string)topTextProp.GetValue(balanceCard)!;
    string BalanceBottom() => (string)bottomTextProp.GetValue(balanceCard)!;
    var applyBtn = (Control)typeof(K11Control.UI.SettingsForm).GetField("_applyBtn", flags)!.GetValue(form)!;
    var applyCurrentEdits = typeof(K11Control.UI.SettingsForm).GetMethod("ApplyCurrentEdits", flags)!;
    var applyTimer = (System.Windows.Forms.Timer)typeof(K11Control.UI.SettingsForm).GetField("_applyFeedbackTimer", flags)!.GetValue(form)!;

    Assert(balanceCard.Parent is { Width: 186 },
        $"Balance alert card lives inside left sidebar across tabs (parent width={balanceCard.Parent?.Width})");

    Assert(!IsBannerShown(),
        "Channel balance card stays hidden when K11 R2R is disconnected even with stale skewed levels");

    form.SetDeviceStatus(true, false, balancedEp);
    Assert(!IsBannerShown(),
        "Channel balance card stays hidden when K11 R2R is connected and L == R");

    form.SetDeviceStatus(true, false, skewedEp);
    Assert(IsBannerShown() && BalanceTop() == "Баланс: -1 / 0" && BalanceBottom() == "Выровнять → 0 дБ",
        $"Sidebar clickable balance button appears when connected and L/R skewed (got '{BalanceTop()}' / '{BalanceBottom()}')");

    // Apply offline vs error vs success feedback without resizing button
    form.SetDeviceStatus(false, false, null);
    Assert(!IsBannerShown(),
        "Channel balance card hides cleanly when K11 R2R disconnects");

    nextApplyResult = false;
    applyCurrentEdits.Invoke(form, [true]);
    Assert(applyBtn.Text == "Нет связи",
        $"Apply button shows Offline text when DAC disconnected and sync fails (got '{applyBtn.Text}')");

    form.SetDeviceStatus(true, false, skewedEp);
    nextApplyResult = false;
    applyCurrentEdits.Invoke(form, [true]);
    Assert(applyBtn.Text == "Ошибка",
        $"Apply button shows Error text when connected DAC sync fails (got '{applyBtn.Text}')");

    nextApplyResult = true;
    applyCurrentEdits.Invoke(form, [true]);
    var borderProp = applyBtn.GetType().GetProperty("BorderColorOverride")!;
    Assert(applyBtn.Text == "Применено ✓" && borderProp.GetValue(applyBtn) is not null,
        $"Apply button shows Applied feedback with emerald border highlight (got '{applyBtn.Text}')");

    // Timer revert restores default Apply label & border
    typeof(System.Windows.Forms.Timer).GetMethod("OnTick", flags)!.Invoke(applyTimer, [EventArgs.Empty]);
    Assert(applyBtn.Text == "Применить" && borderProp.GetValue(applyBtn) is null,
        $"Apply button resets cleanly to default label after feedback timer tick (got '{applyBtn.Text}')");

    // 1-click on whole sidebar balance button invokes handler and hides banner immediately
    typeof(Control).GetMethod("OnClick", flags)!.Invoke(balanceCard, [EventArgs.Empty]);
    Assert(fixCalls == 1 && !IsBannerShown(),
        $"Clicking whole sidebar Balance button invokes handler and hides warning card (calls={fixCalls}, visible={IsBannerShown()})");

    // Check EN labels when switched to English while skewed
    form.ApplyLanguage(AppLanguage.En);
    form.SetDeviceStatus(true, false, skewedEp);
    Assert(IsBannerShown() && BalanceTop() == "Balance: -1 / 0" && BalanceBottom() == "Fix L/R → 0 dB",
        $"English localization updates sidebar balance button labels (got '{BalanceTop()}' / '{BalanceBottom()}')");

    // Switching to another main page clears any transient Apply feedback immediately while sidebar balance stays active
    form.ApplyLanguage(AppLanguage.Ru);
    nextApplyResult = true;
    applyCurrentEdits.Invoke(form, [true]);
    Assert(applyBtn.Text == "Применено ✓", "Apply feedback shown before page switch");
    typeof(K11Control.UI.SettingsForm).GetMethod("SelectPage", flags)!.Invoke(form, [2]);
    Assert(applyBtn.Text == "Применить" && borderProp.GetValue(applyBtn) is null && IsBannerShown(),
        $"Switching to another settings page clears transient Apply feedback while keeping sidebar balance alert visible (got '{applyBtn.Text}')");

    // Verify new left sidebar hierarchy & rounded HotkeyCaptureBox wrapping
    var badge = (Control)typeof(K11Control.UI.SettingsForm).GetField("_connectionBadge", flags)!.GetValue(form)!;
    var muteBtn = (Control)typeof(K11Control.UI.SettingsForm).GetField("_muteBtn", flags)!.GetValue(form)!;
    var rebootBtn = (Control)typeof(K11Control.UI.SettingsForm).GetField("_rebootBtn", flags)!.GetValue(form)!;
    var navList = (System.Collections.IList)typeof(K11Control.UI.SettingsForm).GetField("_navButtons", flags)!.GetValue(form)!;
    var firstNav = (Control)navList[0]!;
    var hkBox = (K11Control.UI.HotkeyCaptureBox)typeof(K11Control.UI.SettingsForm).GetField("_hkToggleOut", flags)!.GetValue(form)!;

    Assert(badge.Bottom < firstNav.Top && muteBtn.Top > balanceCard.Bottom && muteBtn.Height == rebootBtn.Height,
        $"Sidebar hierarchy places passive status above nav ({badge.Bottom} < {firstNav.Top}) and equal-height bottom buttons ({muteBtn.Height} == {rebootBtn.Height})");

    typeof(Control).GetMethod("OnClick", flags)!.Invoke(muteBtn, [EventArgs.Empty]);
    Assert(muteCalls == 1, $"Clicking standalone sidebar Mute button invokes toggle callback (calls={muteCalls})");
    Assert(hkBox.BorderStyle == System.Windows.Forms.BorderStyle.None && hkBox.Parent is { Width: 164, Height: 25 },
        $"HotkeyCaptureBox renders borderless inside rounded wrapper panel ({hkBox.Parent?.Size})");
}

// Background-thread Apply callback must marshal to UI thread so WinForms Timer ticks & resets button
{
    var prevSync = SynchronizationContext.Current;
    SynchronizationContext.SetSynchronizationContext(null);
    try
    {
        var installed = K11Control.UI.TrayApplicationContext.EnsureUiSynchronizationContext();
        Assert(installed is System.Windows.Forms.WindowsFormsSynchronizationContext && SynchronizationContext.Current == installed,
            "EnsureUiSynchronizationContext installs WindowsFormsSynchronizationContext before controls exist");
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(prevSync);
    }

    using var asyncForm = new K11Control.UI.SettingsForm(
        new TrayConfig { Language = AppLanguage.Ru },
        dacConnected: true,
        audioMuted: false,
        _ => { },
        _ => { },
        () => { },
        _ => { },
        () => { },
        onApplyWithFeedback: (_, cb) => Task.Run(() => { Thread.Sleep(15); cb(true); }));
    asyncForm.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
    asyncForm.Location = new System.Drawing.Point(-32000, -32000);
    asyncForm.ShowInTaskbar = false;
    asyncForm.Opacity = 0;
    asyncForm.Show();

    try
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var applyBtn = (Control)typeof(K11Control.UI.SettingsForm).GetField("_applyBtn", flags)!.GetValue(asyncForm)!;
        var applyTimer = (System.Windows.Forms.Timer)typeof(K11Control.UI.SettingsForm).GetField("_applyFeedbackTimer", flags)!.GetValue(asyncForm)!;
        var applyCurrentEdits = typeof(K11Control.UI.SettingsForm).GetMethod("ApplyCurrentEdits", flags)!;
        applyTimer.Interval = 50;

        applyCurrentEdits.Invoke(asyncForm, [true]);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool sawApplied = false;
        bool sawReverted = false;
        while (sw.ElapsedMilliseconds < 2000)
        {
            System.Windows.Forms.Application.DoEvents();
            if (!sawApplied && applyBtn.Text == "Применено ✓")
                sawApplied = true;
            if (sawApplied && applyBtn.Text == "Применить")
            {
                sawReverted = true;
                break;
            }
            Thread.Sleep(5);
        }

        Assert(sawApplied && sawReverted,
            $"Background worker Apply callback marshals to UI thread and auto-resets via WM_TIMER (applied={sawApplied}, reverted={sawReverted}, final='{applyBtn.Text}')");
    }
    finally
    {
        asyncForm.Close();
    }
}

return failures == 0 ? 0 : 1;
