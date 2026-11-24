using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.Versioning;
using K11Control.Audio;
using K11Control.Config;
using K11Control.UI;

namespace K11Control.Protocol;

[Flags]
public enum SyncScope
{
    None = 0,
    OutputMode = 1,
    CustomLcd = 2,
    Rgb = 4,
    All = OutputMode | CustomLcd | Rgb
}

public enum OutputAction : byte
{
    PRE = (byte)OutputMode.PRE,
    PO = (byte)OutputMode.PO,
    Toggle = 0xFF
}

public enum GainAction : byte
{
    Low = (byte)GainMode.Low,
    Mid = (byte)GainMode.Mid,
    High = (byte)GainMode.High,
    Cycle = 0xFF
}

public enum FilterAction : byte
{
    Nos = (byte)FilterMode.Nos,
    Os = (byte)FilterMode.Os,
    Toggle = 0xFF
}

public static class DacCommandProtocol
{
    public const byte CmdSetOutputMode = 0x01;
    public const byte CmdSetCustomLcd = 0x02;
    public const byte CmdSetRgb = 0x03;
    public const byte CmdRebootDac = 0x04;
    public const byte CmdSetGain = 0x05;
    public const byte CmdSetFilter = 0x06;
    public const byte CmdSetCustomLcdMasks = 0x07;
    public const byte CmdSetCustomLcdAnimFrame = 0x08;
    public const byte ChecksumSeed = 0xA5;
    public const int MaxPacketBytes = 16;

    public static byte[] BuildCommandPacket(ReadOnlySpan<byte> body)
    {
        if (body.Length is < 1 or >= MaxPacketBytes)
            throw new ArgumentOutOfRangeException(nameof(body), $"Command body must be 1..{MaxPacketBytes - 1} bytes.");

        byte[] packet = new byte[body.Length + 1];
        byte xor = ChecksumSeed;
        for (int i = 0; i < body.Length; i++)
        {
            packet[i] = body[i];
            xor ^= body[i];
        }
        packet[^1] = xor;
        return packet;
    }

    public static byte[] BuildSetOutputActionPacket(OutputAction action)
    {
        byte hwMode = action switch
        {
            OutputAction.PRE => (byte)OutputAction.PRE,
            OutputAction.Toggle => (byte)OutputAction.Toggle,
            _ => (byte)OutputAction.PO
        };
        return BuildCommandPacket([CmdSetOutputMode, hwMode]);
    }

    public static byte[] BuildSetOutputModePacket(OutputMode mode) =>
        BuildSetOutputActionPacket(mode == OutputMode.PRE ? OutputAction.PRE : OutputAction.PO);

    public static byte[] BuildSetGainActionPacket(GainAction action)
    {
        byte hwGain = action switch
        {
            GainAction.Low => (byte)GainAction.Low,
            GainAction.Mid => (byte)GainAction.Mid,
            GainAction.High => (byte)GainAction.High,
            GainAction.Cycle => (byte)GainAction.Cycle,
            _ => (byte)GainAction.High
        };
        return BuildCommandPacket([CmdSetGain, hwGain]);
    }

    public static byte[] BuildSetFilterActionPacket(FilterAction action)
    {
        byte hwFilter = action switch
        {
            FilterAction.Nos => (byte)FilterAction.Nos,
            FilterAction.Os => (byte)FilterAction.Os,
            FilterAction.Toggle => (byte)FilterAction.Toggle,
            _ => (byte)FilterAction.Os
        };
        return BuildCommandPacket([CmdSetFilter, hwFilter]);
    }

    public static byte[] BuildSetCustomLcdPacket(CustomLcdConfig lcd)
    {
        bool screenOff = lcd.Mode == CustomLcdMode.Normal && lcd.ScreenOff;
        bool hideIcons = lcd.EffectiveHideTopIcons;
        byte flags = (byte)((lcd.IsOverlayActive ? 1 : 0) | (hideIcons ? 2 : 0) | (screenOff ? 4 : 0));
        byte revertDs = (byte)Math.Clamp((int)Math.Round(lcd.ActivityRevertDelayMs / 100.0), 0, 255);
        string formatted = screenOff
            ? new string(' ', 7)
            : lcd.Format7Chars();

        Span<byte> body = stackalloc byte[10];
        body[0] = CmdSetCustomLcd;
        body[1] = flags;
        body[2] = revertDs;
        for (int i = 0; i < 7; i++)
            body[3 + i] = (byte)formatted[i];

        return BuildCommandPacket(body);
    }

    private static void PackCellMasksBody(
        Span<byte> body,
        byte cmdOpcode,
        ReadOnlySpan<ushort> masks,
        bool enabled,
        bool hideTopIcons,
        byte param14,
        byte extraByte13Bits = 0)
    {
        body.Clear();
        body[0] = cmdOpcode;

        for (int cell = 0; cell < 7; cell++)
        {
            ushort mask = cell < masks.Length ? masks[cell] : (ushort)0;
            for (int b = 0; b < 7; b++)
            {
                int idx = cell * 7 + b;
                int pair = (mask >> (b * 2)) & 0x03;
                body[1 + (idx >> 2)] |= (byte)(pair << ((idx & 3) * 2));
            }
        }

        if (enabled)
            body[13] |= 0x04;
        if (hideTopIcons)
            body[13] |= 0x08;
        body[13] |= (byte)(extraByte13Bits & 0xF0);
        body[14] = param14;
    }

    private static bool TryValidateAndUnpackMaskPacket(
        ReadOnlySpan<byte> packet,
        byte expectedCmd,
        out bool enabled,
        out bool hideTopIcons,
        out byte byte13,
        out byte param14,
        out ushort[]? masks)
    {
        enabled = false;
        hideTopIcons = false;
        byte13 = 0;
        param14 = 0;
        masks = null;
        if (packet.Length != MaxPacketBytes || packet[0] != expectedCmd)
            return false;

        byte xor = 0;
        for (int i = 0; i < packet.Length; i++)
            xor ^= packet[i];
        if (xor != ChecksumSeed)
            return false;

        byte13 = packet[13];
        enabled = (byte13 & 0x04) != 0;
        hideTopIcons = (byte13 & 0x08) != 0;
        param14 = packet[14];
        masks = new ushort[7];
        for (int cell = 0; cell < 7; cell++)
        {
            ushort cellMask = 0;
            for (int b = 0; b < 7; b++)
            {
                int idx = cell * 7 + b;
                int pair = (packet[1 + (idx >> 2)] >> ((idx & 3) * 2)) & 0x03;
                cellMask |= (ushort)(pair << (b * 2));
            }
            masks[cell] = cellMask;
        }
        return true;
    }

    public static byte[] BuildSetCustomLcdMasksPacket(CustomLcdConfig lcd)
    {
        ushort[] masks = lcd.ResolveEffectiveMasks();
        byte revertDs = (byte)Math.Clamp((int)Math.Round(lcd.ActivityRevertDelayMs / 100.0), 0, 255);

        Span<byte> body = stackalloc byte[15];
        PackCellMasksBody(body, CmdSetCustomLcdMasks, masks, lcd.IsOverlayActive, lcd.EffectiveHideTopIcons, revertDs);
        return BuildCommandPacket(body);
    }

    public static byte[] BuildSetCustomLcdAnimFramePacket(CustomLcdConfig lcd, int frameIndex)
    {
        int totalFrames = Math.Clamp(lcd.FrameCount, 1, SticksPreset.MaxFrames);
        int idx = Math.Clamp(frameIndex, 0, totalFrames - 1);
        ushort[] masks = lcd.ResolveFrameMasks(idx);
        byte revertDs = (byte)Math.Clamp((int)Math.Round(lcd.ActivityRevertDelayMs / 100.0), 0, 255);
        byte step10Ms = (byte)Math.Clamp((int)Math.Round(lcd.StepIntervalMs / 10.0), 5, 255);
        byte param14 = idx == 0 ? revertDs : step10Ms;
        byte extraBits = (byte)(((idx & 0x03) << 4) | (((totalFrames - 1) & 0x03) << 6));

        Span<byte> body = stackalloc byte[15];
        PackCellMasksBody(body, CmdSetCustomLcdAnimFrame, masks, lcd.IsOverlayActive, lcd.EffectiveHideTopIcons, param14, extraBits);
        return BuildCommandPacket(body);
    }

    public static IReadOnlyList<byte[]> BuildEffectiveLcdPackets(CustomLcdConfig lcd)
    {
        if (lcd.Mode == CustomLcdMode.Segments)
        {
            int count = Math.Clamp(lcd.FrameCount, 1, SticksPreset.MaxFrames);
            if (count >= 2)
            {
                var packets = new byte[count][];
                for (int f = 0; f < count; f++)
                    packets[f] = BuildSetCustomLcdAnimFramePacket(lcd, f);
                return packets;
            }
            return [BuildSetCustomLcdMasksPacket(lcd)];
        }
        if (lcd.Mode == CustomLcdMode.Text && K11LcdFont.RequiresDirectMaskCommand(lcd.Format7Chars()))
            return [BuildSetCustomLcdMasksPacket(lcd)];
        return [BuildSetCustomLcdPacket(lcd)];
    }

    public static byte[] BuildEffectiveLcdPacket(CustomLcdConfig lcd) => BuildEffectiveLcdPackets(lcd)[0];

    public static bool TryDecodeLcdMasksPacket(
        ReadOnlySpan<byte> packet,
        out bool enabled,
        out bool hideTopIcons,
        out byte revertDs,
        out ushort[]? masks) =>
        TryValidateAndUnpackMaskPacket(packet, CmdSetCustomLcdMasks, out enabled, out hideTopIcons, out _, out revertDs, out masks);

    public static bool TryDecodeLcdAnimFramePacket(
        ReadOnlySpan<byte> packet,
        out bool enabled,
        out bool hideTopIcons,
        out int frameIndex,
        out int totalFrames,
        out byte param14,
        out ushort[]? masks)
    {
        frameIndex = 0;
        totalFrames = 1;
        if (!TryValidateAndUnpackMaskPacket(packet, CmdSetCustomLcdAnimFrame, out enabled, out hideTopIcons, out byte b13, out param14, out masks))
            return false;

        frameIndex = (b13 >> 4) & 0x03;
        totalFrames = ((b13 >> 6) & 0x03) + 1;
        return true;
    }

    public static byte[] BuildSetRgbPacket(RgbConfig rgb)
    {
        Span<byte> body = stackalloc byte[12];
        body[0] = CmdSetRgb;

        switch (rgb.State)
        {
            case RgbStateMode.Off:
                body[1] = (byte)RgbEffectMode.Solid;
                body[2] = 0;
                break;

            case RgbStateMode.On:
                body[1] = 0; // Firmware Stock preset
                break;

            case RgbStateMode.Custom:
                body[1] = (byte)Math.Clamp((int)rgb.Effect, (int)RgbEffectMode.Solid, (int)RgbEffectMode.Chase);
                body[2] = (byte)Math.Clamp((int)Math.Round(Math.Clamp(rgb.Brightness, 0, 100) * 2.55), 0, 255);
                // Physical K11 R2R WS2812 daisy-chain runs Right (LED 0, 'O') -> Center (LED 1, 'II') -> Left (LED 2, 'F').
                // Map UI zones [0=Left, 1=Center, 2=Right] -> hardware wire order [2, 1, 0].
                for (int hwLed = 0; hwLed < 3; hwLed++)
                {
                    int uiZone = 2 - hwLed;
                    (byte r, byte g, byte b) = ParseHexRgb(uiZone < rgb.ZoneHexColors.Length ? rgb.ZoneHexColors[uiZone] : null);
                    int off = 3 + hwLed * 3;
                    body[off + 0] = r;
                    body[off + 1] = g;
                    body[off + 2] = b;
                }
                break;
        }

        return BuildCommandPacket(body);
    }

    public static byte[] BuildRebootPacket() => BuildCommandPacket([CmdRebootDac]);

    public static IReadOnlyList<VolumeStep> EncodeUac2Transfer(
        ReadOnlySpan<byte> packet,
        float savedCh0Db,
        float savedCh1Db,
        int interWriteMs = 3)
    {
        if (packet.Length is < 2 or > MaxPacketBytes)
            throw new ArgumentOutOfRangeException(nameof(packet), $"Packet length must be 2..{MaxPacketBytes}.");

        var steps = new List<VolumeStep>(6 + packet.Length * 2);

        // Ensure Windows CoreAudio generates actual USB EP0 SET_CUR transitions for Knock 1 & Knock 2
        if (MathF.Abs(savedCh1Db - (-63f)) < 0.5f)
            steps.Add(new VolumeStep(1, -62f, interWriteMs));
        if (MathF.Abs(savedCh0Db - (-62f)) < 0.5f)
            steps.Add(new VolumeStep(0, -63f, interWriteMs));

        // 3-step zero-click escape preamble (suppressed in firmware i2c2_rx_hook before vol_calc)
        steps.Add(new VolumeStep(0, -62f, interWriteMs));
        steps.Add(new VolumeStep(1, -63f, interWriteMs));
        steps.Add(new VolumeStep(0, -61f, interWriteMs));

        float lastCh1Db = -63f;
        int nibIdx = 0;
        foreach (byte b in packet)
        {
            for (int shift = 4; shift >= 0; shift -= 4)
            {
                int nib = (b >> shift) & 0x0F;
                uint channel = (uint)(nibIdx & 1);
                int toggle = (nibIdx >> 1) & 1;
                float levelDb = -(1 + (toggle << 4) + nib);
                steps.Add(new VolumeStep(channel, levelDb, interWriteMs));
                if (channel == 1)
                    lastCh1Db = levelDb;
                nibIdx++;
            }
        }

        // Commit sentinel on Ch0 (-60 dB)
        steps.Add(new VolumeStep(0, -60f, Math.Max(interWriteMs, 6)));

        // Restore original OS volume levels (post-commit, CMD_ACTIVE is cleared on DAC)
        float r0 = MathF.Round(Math.Clamp(savedCh0Db, -63f, 0f));
        float r1 = MathF.Round(Math.Clamp(savedCh1Db, -63f, 0f));
        if (MathF.Abs(r0 - (-60f)) >= 0.5f)
            steps.Add(new VolumeStep(0, r0, 2));
        if (MathF.Abs(r1 - lastCh1Db) >= 0.5f)
            steps.Add(new VolumeStep(1, r1, 2));

        return steps;
    }

    /// <summary>
    /// Reference state machine matching firmware M79 i2c2_rx_hook (for offline protocol verification).
    /// </summary>
    public static bool TryDecodeReferenceStream(IEnumerable<VolumeStep> steps, out byte[]? packet)
    {
        packet = null;
        int knockStage = 0;
        bool cmdActive = false;
        int nibIdx = 0;
        byte nibTmp = 0;
        Span<byte> buf = stackalloc byte[MaxPacketBytes];

        foreach (VolumeStep step in steps)
        {
            int k = Math.Clamp(-(int)MathF.Round(step.LevelDb), 0, 255);
            if (!cmdActive)
            {
                if (knockStage == 0)
                {
                    if (step.Channel == 0 && k == 62)
                        knockStage = 1;
                }
                else if (knockStage == 1)
                {
                    knockStage = (step.Channel == 1 && k == 63) ? 2 : 0;
                }
                else
                {
                    knockStage = 0;
                    if (step.Channel == 0 && k == 61)
                    {
                        cmdActive = true;
                        nibIdx = 0;
                    }
                }
                continue;
            }

            if (step.Channel == 0 && k == 60)
            {
                cmdActive = false;
                if ((nibIdx & 1) != 0 || nibIdx < 4 || nibIdx > 32)
                    return false;
                int len = nibIdx >> 1;
                byte xor = 0;
                for (int i = 0; i < len; i++)
                    xor ^= buf[i];
                if (xor != ChecksumSeed)
                    return false;
                packet = buf[..len].ToArray();
                return true;
            }

            if (k is < 1 or > 32 || nibIdx >= 32)
                return false;

            int v = k - 1;
            if ((step.Channel & 1) != (uint)(nibIdx & 1))
                return false;
            if ((v >> 4) != ((nibIdx >> 1) & 1))
                return false;

            byte nib = (byte)(v & 0x0F);
            if ((nibIdx & 1) == 0)
                nibTmp = (byte)(nib << 4);
            else
                buf[nibIdx >> 1] = (byte)(nibTmp | nib);
            nibIdx++;
        }

        return false;
    }

    public static (byte R, byte G, byte B) ParseHexRgb(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return (0, 0xB4, 0xFF);
        ReadOnlySpan<char> span = hex.AsSpan().Trim();
        if (span.StartsWith("#"))
            span = span[1..];
        if (span.Length == 6
            && byte.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
            && byte.TryParse(span.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
            && byte.TryParse(span.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return (r, g, b);
        }
        return (0, 0xB4, 0xFF);
    }
}

[SupportedOSPlatform("windows")]
public sealed class DacCommandClient : IDisposable
{
    private sealed record QueuedJob(
        string Reason,
        SyncScope Scope,
        TrayConfig? Snapshot,
        byte[]? DirectPacket,
        Action<bool, string?>? Callback);

    private readonly CoreAudioVolumeService _audio;
    private readonly BlockingCollection<QueuedJob> _queue = new();
    private readonly Thread _workerThread;
    private bool _disposed;

    public DacCommandClient(CoreAudioVolumeService audio)
    {
        _audio = audio;
        _workerThread = new Thread(RunWorker)
        {
            IsBackground = true,
            Name = "K11Control.DacCommandWorker"
        };
        _workerThread.SetApartmentState(ApartmentState.STA);
        _workerThread.Start();
    }

    public void EnqueueSync(TrayConfig config, SyncScope scope, string reason, Action<bool, string?>? onCompleted = null)
    {
        if (_disposed || scope == SyncScope.None)
            return;
        _queue.Add(new QueuedJob(reason, scope, CloneSnapshot(config), DirectPacket: null, onCompleted));
    }

    public void EnqueueOutputAction(OutputAction action, string reason, Action<bool, string?>? onCompleted = null) =>
        EnqueueDirectPacket(DacCommandProtocol.BuildSetOutputActionPacket(action), reason, onCompleted);

    public void EnqueueGainAction(GainAction action, string reason, Action<bool, string?>? onCompleted = null) =>
        EnqueueDirectPacket(DacCommandProtocol.BuildSetGainActionPacket(action), reason, onCompleted);

    public void EnqueueFilterAction(FilterAction action, string reason, Action<bool, string?>? onCompleted = null) =>
        EnqueueDirectPacket(DacCommandProtocol.BuildSetFilterActionPacket(action), reason, onCompleted);

    public void EnqueueReboot(Action<bool, string?>? onCompleted = null) =>
        EnqueueDirectPacket(DacCommandProtocol.BuildRebootPacket(), "Reboot", onCompleted);

    private void EnqueueDirectPacket(byte[] packet, string reason, Action<bool, string?>? onCompleted)
    {
        if (_disposed)
            return;
        _queue.Add(new QueuedJob(reason, SyncScope.None, Snapshot: null, packet, onCompleted));
    }

    public bool TrySendPacketSync(byte[] packet, out string? error) =>
        _audio.TryExecuteVolumeSequence(
            (ch0, ch1) => DacCommandProtocol.EncodeUac2Transfer(packet, ch0, ch1),
            out error);

    private void RunWorker()
    {
        foreach (QueuedJob job in _queue.GetConsumingEnumerable())
        {
            try
            {
                if (job.DirectPacket is byte[] directPacket)
                {
                    bool ok = TrySendPacketSync(directPacket, out string? err);
                    job.Callback?.Invoke(ok, err);
                    continue;
                }

                if (job.Snapshot is null)
                    continue;

                bool allOk = true;
                string? lastErr = null;
                if (job.Scope.HasFlag(SyncScope.OutputMode))
                {
                    allOk &= TrySendPacketSync(
                        DacCommandProtocol.BuildSetOutputModePacket(job.Snapshot.PreferredOutput),
                        out string? e1);
                    lastErr ??= e1;
                    // Allow relay & native UI redraw triggered by set_out_mode_helper to settle before LCD/RGB follow-up.
                    if (job.Scope != SyncScope.OutputMode)
                        Thread.Sleep(100);
                }

                if (job.Scope.HasFlag(SyncScope.Rgb))
                {
                    allOk &= TrySendPacketSync(
                        DacCommandProtocol.BuildSetRgbPacket(job.Snapshot.Rgb),
                        out string? e2);
                    lastErr ??= e2;
                    if (job.Scope.HasFlag(SyncScope.CustomLcd))
                        Thread.Sleep(30);
                }

                if (job.Scope.HasFlag(SyncScope.CustomLcd))
                {
                    IReadOnlyList<byte[]> lcdPackets = DacCommandProtocol.BuildEffectiveLcdPackets(job.Snapshot.CustomLcd);
                    for (int i = 0; i < lcdPackets.Count; i++)
                    {
                        allOk &= TrySendPacketSync(lcdPackets[i], out string? e3);
                        lastErr ??= e3;
                        if (i + 1 < lcdPackets.Count)
                            Thread.Sleep(25);
                    }
                }

                job.Callback?.Invoke(allOk, lastErr);
            }
            catch (Exception ex)
            {
                job.Callback?.Invoke(false, ex.Message);
            }
        }
    }

    private static TrayConfig CloneSnapshot(TrayConfig src) => new()
    {
        PreferredOutput = src.PreferredOutput,
        EnforceOutputOnConnect = src.EnforceOutputOnConnect,
        CustomLcd = new CustomLcdConfig
        {
            Mode = src.CustomLcd.Mode,
            LastCustomMode = src.CustomLcd.LastCustomMode,
            HideTopIconsNormal = src.CustomLcd.HideTopIconsNormal,
            HideTopIconsText = src.CustomLcd.HideTopIconsText,
            HideTopIconsSegments = src.CustomLcd.HideTopIconsSegments,
            ScreenOff = src.CustomLcd.ScreenOff,
            Text = src.CustomLcd.Text,
            SegmentMasks = src.CustomLcd.SegmentMasks.ToArray(),
            FrameCount = src.CustomLcd.FrameCount,
            StepIntervalMs = src.CustomLcd.StepIntervalMs,
            AnimationFrames = SticksPreset.NormalizeFrames(src.CustomLcd.AnimationFrames),
            ActiveSticksPresetIndex = src.CustomLcd.ActiveSticksPresetIndex,
            SticksPresets = src.CustomLcd.SticksPresets.Select(p => p.Clone()).ToList(),
            ActivityRevertDelayMs = src.CustomLcd.ActivityRevertDelayMs
        },
        Rgb = new RgbConfig
        {
            State = src.Rgb.State,
            Effect = src.Rgb.Effect,
            Brightness = src.Rgb.Brightness,
            ZoneHexColors = src.Rgb.ZoneHexColors.ToArray()
        }
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _queue.CompleteAdding();
        if (!_workerThread.Join(1000))
            _ = 0;
        _queue.Dispose();
    }
}
