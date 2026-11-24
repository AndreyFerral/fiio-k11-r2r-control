using System.Text.Json;
using System.Text.Json.Serialization;
using K11Control.UI;

namespace K11Control.Config;

public enum OutputMode
{
    PO = 4,
    PRE = 2
    // LO (1) is intentionally excluded for speaker/hearing safety.
}

public enum GainMode
{
    Low = 0,
    Mid = 1,
    High = 2
}

public enum FilterMode
{
    Nos = 0,
    Os = 1
}

public enum AppLanguage
{
    Ru = 0,
    En = 1
}

public enum RgbStateMode
{
    Off = 0,
    On = 1,
    Custom = 2
}

public enum RgbEffectMode
{
    Solid = 1,
    Breathing = 2,
    Rainbow = 3,
    Chase = 4
}

public enum CustomLcdMode
{
    Normal = 0,
    Text = 1,
    Segments = 2
}

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
    NoRepeat = 0x4000
}

public sealed class HotkeyBinding
{
    public HotkeyModifiers Modifiers { get; set; } = HotkeyModifiers.None;
    public Keys Key { get; set; } = Keys.None;

    [JsonIgnore]
    public bool IsConfigured => Key != Keys.None;

    public bool Matches(HotkeyModifiers mods, Keys key) => Modifiers == mods && Key == key;

    public override string ToString()
    {
        if (!IsConfigured)
            return "(None)";

        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(FormatKeyName(Key));
        return string.Join("+", parts);
    }

    private static string FormatKeyName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => $"Num{(int)(key - Keys.NumPad0)}",
        _ => key.ToString()
    };
}

public sealed class HotkeyConfig
{
    public HotkeyBinding ToggleOutput { get; set; } = new()
    {
        Modifiers = HotkeyModifiers.Alt,
        Key = Keys.D1
    };

    public HotkeyBinding SwitchToPre { get; set; } = new()
    {
        Modifiers = HotkeyModifiers.Alt,
        Key = Keys.D2
    };

    public HotkeyBinding SwitchToPo { get; set; } = new()
    {
        Modifiers = HotkeyModifiers.Alt,
        Key = Keys.D3
    };

    public HotkeyBinding ToggleCustomText { get; set; } = new()
    {
        Modifiers = HotkeyModifiers.Alt,
        Key = Keys.D4
    };

    public HotkeyBinding ToggleRgb { get; set; } = new()
    {
        Modifiers = HotkeyModifiers.Alt,
        Key = Keys.D5
    };

    public HotkeyBinding ToggleMute { get; set; } = new();

    public HotkeyBinding CycleGain { get; set; } = new();

    public HotkeyBinding ToggleFilter { get; set; } = new();

    public HotkeyBinding RebootDac { get; set; } = new();
}

public sealed class SticksPreset
{
    public const int MaxFrames = 4;

    public string Name { get; set; } = "Preset";
    public bool IsBuiltIn { get; set; } = false;
    public SticksTemplateKind Template { get; set; } = SticksTemplateKind.Custom;
    public string InnerText { get; set; } = "";
    public int FrameCount { get; set; } = 1;
    public int StepIntervalMs { get; set; } = 300;
    public ushort[][] Frames { get; set; } = CreateEmptyFrames();

    public static ushort[][] CreateEmptyFrames()
    {
        var arr = new ushort[MaxFrames][];
        for (int i = 0; i < MaxFrames; i++)
            arr[i] = new ushort[7];
        return arr;
    }

    public static ushort[][] NormalizeFrames(ushort[][]? src)
    {
        var norm = CreateEmptyFrames();
        if (src is null)
            return norm;

        for (int f = 0; f < Math.Min(MaxFrames, src.Length); f++)
        {
            if (src[f] is null)
                continue;
            for (int c = 0; c < Math.Min(7, src[f].Length); c++)
                norm[f][c] = (ushort)(src[f][c] & K11LcdFont.AllSegmentsMask);
        }
        return norm;
    }

    public void Normalize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Preset" : Name.Trim();
        if (!Enum.IsDefined(Template))
            Template = SticksTemplateKind.Custom;
        InnerText = CustomLcdConfig.SanitizeSegment(InnerText ?? "", 5).TrimEnd();
        FrameCount = Math.Clamp(FrameCount, 1, MaxFrames);
        StepIntervalMs = Math.Clamp(StepIntervalMs, 150, 1500);
        if (IsBuiltIn)
        {
            if (Template == SticksTemplateKind.Custom)
            {
                IsBuiltIn = false;
                Frames = NormalizeFrames(Frames);
                return;
            }
            // Keep stock segment geometry authoritative across updates while preserving InnerText and StepIntervalMs
            var (builtFrames, builtCount, _) = SticksTemplateEngine.Build(Template, InnerText);
            Frames = builtFrames;
            FrameCount = builtCount;
            return;
        }
        Frames = NormalizeFrames(Frames);
    }

    public SticksPreset Clone()
    {
        Normalize();
        var copyFrames = CreateEmptyFrames();
        for (int f = 0; f < MaxFrames; f++)
            Array.Copy(Frames[f], copyFrames[f], 7);
        return new SticksPreset
        {
            Name = Name,
            IsBuiltIn = IsBuiltIn,
            Template = Template,
            InnerText = InnerText,
            FrameCount = FrameCount,
            StepIntervalMs = StepIntervalMs,
            Frames = copyFrames
        };
    }

    public static SticksPreset FromTemplate(string name, SticksTemplateKind kind, string? innerText = null, bool isBuiltIn = false)
    {
        string text = SticksTemplateEngine.SupportsInnerText(kind)
            ? CustomLcdConfig.SanitizeSegment(innerText ?? SticksTemplateEngine.DefaultInnerText(kind), 5).TrimEnd()
            : "";
        var (frames, count, stepMs) = SticksTemplateEngine.Build(kind, text);
        return new SticksPreset
        {
            Name = name,
            IsBuiltIn = isBuiltIn && kind != SticksTemplateKind.Custom,
            Template = kind,
            InnerText = text,
            FrameCount = count,
            StepIntervalMs = stepMs,
            Frames = frames
        };
    }
}

public sealed class CustomLcdConfig
{
    /// <summary>Exact 43-character glyph set present in stock M79 firmware 0x0800DB68.</summary>
    public const string SupportedGlyphs = K11LcdFont.StockM79Glyphs;

    private CustomLcdMode _mode = CustomLcdMode.Normal;
    private CustomLcdMode _lastCustomMode = CustomLcdMode.Text;

    public CustomLcdMode Mode
    {
        get => _mode;
        set
        {
            _mode = Enum.IsDefined(value) ? value : CustomLcdMode.Normal;
            if (_mode != CustomLcdMode.Normal)
                _lastCustomMode = _mode;
        }
    }

    public CustomLcdMode LastCustomMode
    {
        get => _lastCustomMode == CustomLcdMode.Segments ? CustomLcdMode.Segments : CustomLcdMode.Text;
        set => _lastCustomMode = value == CustomLcdMode.Segments ? CustomLcdMode.Segments : CustomLcdMode.Text;
    }

    [JsonIgnore]
    public bool Enabled
    {
        get => _mode != CustomLcdMode.Normal;
        set => Mode = value ? LastCustomMode : CustomLcdMode.Normal;
    }

    [JsonIgnore]
    public bool IsOverlayActive => _mode != CustomLcdMode.Normal || ScreenOff;

    [JsonIgnore]
    public bool EffectiveHideTopIcons => GetHideTopIcons(_mode);

    [JsonIgnore]
    public bool RequiresRamSync => IsOverlayActive || EffectiveHideTopIcons;

    [JsonPropertyName("Enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyEnabled { get; set; }

    [JsonPropertyName("HideTopIcons")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyHideTopIcons { get; set; }

    [JsonPropertyName("HideNormalText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyHideNormalText { get; set; }

    [JsonIgnore]
    public bool HideTopIcons
    {
        get => GetHideTopIcons(_mode);
        set => SetHideTopIcons(_mode, value);
    }

    public bool HideTopIconsNormal { get; set; } = false;
    public bool HideTopIconsText { get; set; } = false;
    public bool HideTopIconsSegments { get; set; } = false;
    public bool ScreenOff { get; set; } = false;

    public bool GetHideTopIcons(CustomLcdMode mode) => mode switch
    {
        CustomLcdMode.Text => HideTopIconsText,
        CustomLcdMode.Segments => HideTopIconsSegments,
        _ => HideTopIconsNormal
    };

    public void SetHideTopIcons(CustomLcdMode mode, bool value)
    {
        switch (mode)
        {
            case CustomLcdMode.Text: HideTopIconsText = value; break;
            case CustomLcdMode.Segments: HideTopIconsSegments = value; break;
            default: HideTopIconsNormal = value; break;
        }
    }
    public string Text { get; set; } = "K11 R2R";
    public ushort[] SegmentMasks { get; set; } = new ushort[7];
    public int FrameCount { get; set; } = 1;
    public int StepIntervalMs { get; set; } = 300;
    public ushort[][] AnimationFrames { get; set; } = SticksPreset.CreateEmptyFrames();
    public List<SticksPreset> SticksPresets { get; set; } = [];
    public int ActiveSticksPresetIndex { get; set; } = 0;
    public int ActivityRevertDelayMs { get; set; } = 2500;

    // Legacy 2-zone fields kept only to migrate existing config.json cleanly on load.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TopLinePo { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TopLinePre { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BottomText { get; set; }

    public void MigrateLegacyIfNeeded()
    {
        if (TopLinePo is not null || BottomText is not null || TopLinePre is not null)
        {
            if (!string.IsNullOrEmpty(TopLinePo) || !string.IsNullOrEmpty(BottomText))
            {
                string left = SanitizeSegment(TopLinePo, 4);
                string right = SanitizeSegment(BottomText, 3);
                Text = (left + right).TrimEnd();
            }
            TopLinePo = null;
            TopLinePre = null;
            BottomText = null;
        }

        if (LegacyEnabled.HasValue)
        {
            if (_mode == CustomLcdMode.Normal && LegacyEnabled.Value)
                Mode = CustomLcdMode.Text;
            LegacyEnabled = null;
        }

        if (LegacyHideTopIcons.HasValue)
        {
            HideTopIconsNormal = LegacyHideTopIcons.Value;
            HideTopIconsText = LegacyHideTopIcons.Value;
            HideTopIconsSegments = LegacyHideTopIcons.Value;
            LegacyHideTopIcons = null;
        }

        if (LegacyHideNormalText.HasValue)
        {
            if (LegacyHideNormalText.Value)
                ScreenOff = true;
            LegacyHideNormalText = null;
        }

        if (!Enum.IsDefined(_mode))
            _mode = CustomLcdMode.Normal;
        _lastCustomMode = _lastCustomMode == CustomLcdMode.Segments ? CustomLcdMode.Segments : CustomLcdMode.Text;

        var normalizedMasks = new ushort[7];
        if (SegmentMasks is not null)
        {
            for (int i = 0; i < Math.Min(7, SegmentMasks.Length); i++)
                normalizedMasks[i] = (ushort)(SegmentMasks[i] & K11LcdFont.AllSegmentsMask);
        }
        SegmentMasks = normalizedMasks;
        FrameCount = Math.Clamp(FrameCount, 1, SticksPreset.MaxFrames);
        StepIntervalMs = Math.Clamp(StepIntervalMs, 150, 1500);
        AnimationFrames = SticksPreset.NormalizeFrames(AnimationFrames);
        if (SegmentMasks.Any(m => m != 0) && AnimationFrames[0].All(m => m == 0))
            Array.Copy(SegmentMasks, AnimationFrames[0], 7);
        else if (AnimationFrames[0].Any(m => m != 0))
            Array.Copy(AnimationFrames[0], SegmentMasks, 7);

        SticksPresets ??= [];
        foreach (var preset in SticksPresets)
            preset.Normalize();
        if (SticksPresets.Count > 0)
            ActiveSticksPresetIndex = Math.Clamp(ActiveSticksPresetIndex, 0, SticksPresets.Count - 1);
        else
            ActiveSticksPresetIndex = 0;

        ActivityRevertDelayMs = Math.Clamp(ActivityRevertDelayMs, 500, 30000);
    }

    public void EnsureSticksPresetsInitialized(AppLanguage lang)
    {
        SticksPresets ??= [];
        SticksPreset? activeRef = (ActiveSticksPresetIndex >= 0 && ActiveSticksPresetIndex < SticksPresets.Count)
            ? SticksPresets[ActiveSticksPresetIndex]
            : null;

        if (SticksPresets.Count == 0 && (SegmentMasks.Any(m => m != 0) || FrameCount > 1))
        {
            var carried = new SticksPreset
            {
                Name = AppStrings.SticksStarterCustomName(lang, 1),
                Template = SticksTemplateKind.Custom,
                FrameCount = Math.Clamp(FrameCount, 1, SticksPreset.MaxFrames),
                StepIntervalMs = Math.Clamp(StepIntervalMs, 150, 1500),
                Frames = SticksPreset.NormalizeFrames(AnimationFrames)
            };
            if (carried.Frames[0].All(m => m == 0))
                Array.Copy(SegmentMasks, carried.Frames[0], 7);
            SticksPresets.Add(carried);
        }
        else if (SticksPresets.Count > 0 && !SticksPresets.Any(p => p.IsBuiltIn))
        {
            // Promote pre-existing starter entries saved before IsBuiltIn existed without touching user presets
            foreach (var p in SticksPresets)
            {
                if (p.Template == SticksTemplateKind.Custom)
                    continue;
                if (string.Equals(p.Name, AppStrings.SticksStarterPresetName(AppLanguage.Ru, p.Template), StringComparison.Ordinal)
                    || string.Equals(p.Name, AppStrings.SticksStarterPresetName(AppLanguage.En, p.Template), StringComparison.Ordinal))
                {
                    p.IsBuiltIn = true;
                }
            }
        }

        // Always keep all 7 built-in starter templates present even if an earlier build edited/deleted one
        for (int t = 0; t < SticksTemplateEngine.OrderedTemplates.Length; t++)
        {
            SticksTemplateKind kind = SticksTemplateEngine.OrderedTemplates[t];
            if (kind == SticksTemplateKind.Custom || SticksPresets.Any(p => p.IsBuiltIn && p.Template == kind))
                continue;

            int insertAt = -1;
            int lastBuiltInIdx = -1;
            for (int i = 0; i < SticksPresets.Count; i++)
            {
                var p = SticksPresets[i];
                if (!p.IsBuiltIn)
                    continue;
                lastBuiltInIdx = i;
                int pOrder = Array.IndexOf(SticksTemplateEngine.OrderedTemplates, p.Template);
                if (pOrder > t)
                {
                    insertAt = i;
                    break;
                }
            }
            if (insertAt < 0)
                insertAt = lastBuiltInIdx >= 0 ? lastBuiltInIdx + 1 : SticksPresets.Count;

            SticksPresets.Insert(insertAt, SticksPreset.FromTemplate(AppStrings.SticksStarterPresetName(lang, kind), kind, isBuiltIn: true));
        }

        foreach (var preset in SticksPresets)
        {
            preset.Normalize();
            if (preset.IsBuiltIn && preset.Template != SticksTemplateKind.Custom)
                preset.Name = AppStrings.SticksStarterPresetName(lang, preset.Template);
        }

        if (activeRef is not null)
        {
            int idx = SticksPresets.IndexOf(activeRef);
            ActiveSticksPresetIndex = idx >= 0 ? idx : Math.Clamp(ActiveSticksPresetIndex, 0, SticksPresets.Count - 1);
        }
        else
        {
            ActiveSticksPresetIndex = 0;
        }

        if (FrameCount <= 1 && SegmentMasks.All(m => m == 0) && AnimationFrames[0].All(m => m == 0))
            ApplyPresetToLive(SticksPresets[ActiveSticksPresetIndex]);
    }

    public void ApplyPresetToLive(SticksPreset preset)
    {
        preset.Normalize();
        FrameCount = preset.FrameCount;
        StepIntervalMs = preset.StepIntervalMs;
        AnimationFrames = SticksPreset.NormalizeFrames(preset.Frames);
        SegmentMasks = AnimationFrames[0].ToArray();
    }

    public static string SanitizeSegment(string? input, int maxLen)
    {
        var chars = new char[maxLen];
        Array.Fill(chars, ' ');
        if (string.IsNullOrEmpty(input))
            return new string(chars);

        int idx = 0;
        foreach (char raw in input)
        {
            if (idx >= maxLen)
                break;

            chars[idx++] = K11LcdFont.ResolveDisplayChar(raw);
        }
        return new string(chars);
    }

    public string Format7Chars() => SanitizeSegment(Text, 7);

    public ushort[] ResolveFrameMasks(int frameIndex)
    {
        if (Mode == CustomLcdMode.Segments)
        {
            int idx = Math.Clamp(frameIndex, 0, SticksPreset.MaxFrames - 1);
            if (idx == 0 && (AnimationFrames is null || AnimationFrames[0].All(m => m == 0)) && SegmentMasks.Any(m => m != 0))
            {
                var fallback = new ushort[7];
                for (int i = 0; i < Math.Min(7, SegmentMasks.Length); i++)
                    fallback[i] = (ushort)(SegmentMasks[i] & K11LcdFont.AllSegmentsMask);
                return fallback;
            }

            var result = new ushort[7];
            if (AnimationFrames is not null && idx < AnimationFrames.Length && AnimationFrames[idx] is not null)
            {
                for (int i = 0; i < Math.Min(7, AnimationFrames[idx].Length); i++)
                    result[i] = (ushort)(AnimationFrames[idx][i] & K11LcdFont.AllSegmentsMask);
            }
            return result;
        }
        return K11LcdFont.BuildMasksFromText(Format7Chars());
    }

    public ushort[] ResolveEffectiveMasks() => ResolveFrameMasks(0);
}

public sealed class RgbConfig
{
    public RgbStateMode State { get; set; } = RgbStateMode.On;
    public RgbEffectMode Effect { get; set; } = RgbEffectMode.Solid;
    public int Brightness { get; set; } = 80;
    public string[] ZoneHexColors { get; set; } = ["#00B4FF", "#00B4FF", "#00B4FF"];

    // Legacy v1 fields migrated cleanly on load.
    [JsonPropertyName("Enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyEnabled { get; set; }

    [JsonPropertyName("Mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyMode { get; set; }

    public void MigrateLegacyIfNeeded()
    {
        if (LegacyEnabled is not null || LegacyMode is not null)
        {
            if (LegacyEnabled == false)
            {
                State = RgbStateMode.Off;
            }
            else if (string.Equals(LegacyMode, "Stock", StringComparison.OrdinalIgnoreCase))
            {
                State = RgbStateMode.On;
            }
            else if (Enum.TryParse<RgbEffectMode>(LegacyMode, ignoreCase: true, out var parsedEffect)
                && Enum.IsDefined(parsedEffect))
            {
                State = RgbStateMode.Custom;
                Effect = parsedEffect;
            }
            else if (LegacyEnabled == true)
            {
                State = RgbStateMode.On;
            }
            LegacyEnabled = null;
            LegacyMode = null;
        }

        if (!Enum.IsDefined(Effect))
            Effect = RgbEffectMode.Solid;
        if (!Enum.IsDefined(State))
            State = RgbStateMode.On;
        Brightness = Math.Clamp(Brightness, 0, 100);
        if (ZoneHexColors is null || ZoneHexColors.Length < 3)
        {
            var prev = ZoneHexColors ?? [];
            ZoneHexColors =
            [
                prev.Length > 0 && !string.IsNullOrWhiteSpace(prev[0]) ? prev[0] : "#00B4FF",
                prev.Length > 1 && !string.IsNullOrWhiteSpace(prev[1]) ? prev[1] : "#00B4FF",
                prev.Length > 2 && !string.IsNullOrWhiteSpace(prev[2]) ? prev[2] : "#00B4FF"
            ];
        }
    }
}

public sealed class TrayConfig
{
    public const int CurrentConfigVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public int ConfigVersion { get; set; } = CurrentConfigVersion;
    public AppLanguage Language { get; set; } = AppLanguage.Ru;

    /// <summary>Target mode when EnforceOutputOnConnect is enabled (never reflects unpolled DAC knob state).</summary>
    public OutputMode PreferredOutput { get; set; } = OutputMode.PO;
    public bool EnforceOutputOnConnect { get; set; } = false;
    public HotkeyConfig Hotkeys { get; set; } = new();
    public CustomLcdConfig CustomLcd { get; set; } = new();
    public RgbConfig Rgb { get; set; } = new();

    public static string DefaultConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "K11Control",
            "config.json");

    public static TrayConfig Load(string? path = null)
    {
        string target = path ?? DefaultConfigPath;
        if (!File.Exists(target))
            return new TrayConfig();

        string json = File.ReadAllText(target);
        var cfg = JsonSerializer.Deserialize<TrayConfig>(json, JsonOptions) ?? new TrayConfig();
        bool missingVersion = !json.Contains("\"ConfigVersion\"", StringComparison.Ordinal);

        cfg.CustomLcd.MigrateLegacyIfNeeded();
        cfg.Rgb.MigrateLegacyIfNeeded();

        if (missingVersion || cfg.ConfigVersion < CurrentConfigVersion)
        {
            cfg.MigrateV1Hotkeys();
            cfg.ConfigVersion = CurrentConfigVersion;
        }

        // Enforce safety invariants.
        if (cfg.PreferredOutput != OutputMode.PO && cfg.PreferredOutput != OutputMode.PRE)
            cfg.PreferredOutput = OutputMode.PO;
        if (!Enum.IsDefined(cfg.Language))
            cfg.Language = AppLanguage.Ru;
        return cfg;
    }

    private void MigrateV1Hotkeys()
    {
        var ctrlAlt = HotkeyModifiers.Control | HotkeyModifiers.Alt;
        if (!Hotkeys.ToggleOutput.IsConfigured || Hotkeys.ToggleOutput.Matches(ctrlAlt, Keys.O))
            Hotkeys.ToggleOutput = new HotkeyBinding { Modifiers = HotkeyModifiers.Alt, Key = Keys.D1 };
        if (!Hotkeys.SwitchToPre.IsConfigured)
            Hotkeys.SwitchToPre = new HotkeyBinding { Modifiers = HotkeyModifiers.Alt, Key = Keys.D2 };
        if (!Hotkeys.SwitchToPo.IsConfigured)
            Hotkeys.SwitchToPo = new HotkeyBinding { Modifiers = HotkeyModifiers.Alt, Key = Keys.D3 };
        if (!Hotkeys.ToggleCustomText.IsConfigured || Hotkeys.ToggleCustomText.Matches(ctrlAlt, Keys.T))
            Hotkeys.ToggleCustomText = new HotkeyBinding { Modifiers = HotkeyModifiers.Alt, Key = Keys.D4 };
        if (!Hotkeys.ToggleRgb.IsConfigured || Hotkeys.ToggleRgb.Matches(ctrlAlt, Keys.L))
            Hotkeys.ToggleRgb = new HotkeyBinding { Modifiers = HotkeyModifiers.Alt, Key = Keys.D5 };
    }

    public void Save(string? path = null)
    {
        ConfigVersion = CurrentConfigVersion;
        string target = path ?? DefaultConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(target, json);
    }
}
