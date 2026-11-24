using System.Runtime.Versioning;
using K11Control.Audio;
using K11Control.Config;
using K11Control.Usb;

namespace K11Control.Stage0;

[SupportedOSPlatform("windows")]
public static class Stage0ProbeRunner
{
    public static int RunCli(string[] args)
    {
        if (args.Length == 0 || args[0] is "probe-status" or "status")
        {
            Console.WriteLine(BuildStatusReport());
            return 0;
        }

        string cmd = args[0];
        var audio = new CoreAudioVolumeService();
        var hid = new K11HidMonitor();

        switch (cmd)
        {
            case "probe-uac-ch0":
                if (args.Length < 2 || !float.TryParse(args[1], out float db0))
                {
                    Console.Error.WriteLine("Usage: --stage0-probe probe-uac-ch0 <dB>");
                    return 2;
                }
                Console.WriteLine(audio.TrySetChannelVolumeDb(0, db0)
                    ? $"Set Ch0 to {db0} dB"
                    : "K11 R2R CoreAudio endpoint not found.");
                return 0;

            case "probe-uac-ch1":
                if (args.Length < 2 || !float.TryParse(args[1], out float db1))
                {
                    Console.Error.WriteLine("Usage: --stage0-probe probe-uac-ch1 <dB>");
                    return 2;
                }
                Console.WriteLine(audio.TrySetChannelVolumeDb(1, db1)
                    ? $"Set Ch1 to {db1} dB"
                    : "K11 R2R CoreAudio endpoint not found.");
                return 0;

            case "probe-uac-mute":
                if (args.Length < 2 || (args[1] != "0" && args[1] != "1"))
                {
                    Console.Error.WriteLine("Usage: --stage0-probe probe-uac-mute <0|1>");
                    return 2;
                }
                bool m = args[1] == "1";
                Console.WriteLine(audio.TrySetMute(m)
                    ? $"Set Master Mute to {m}"
                    : "K11 R2R CoreAudio endpoint not found.");
                return 0;

            case "probe-hid-col01":
                hid.TrySendStage0Col01Probe([0xAA, 0x55, 0x01, 0x02], out string msg1);
                Console.WriteLine(msg1);
                return 0;

            case "probe-hid-col02":
                hid.TrySendStage0Col02FeatureProbe([0xBB, 0x66, 0x03, 0x04], out string msg2);
                Console.WriteLine(msg2);
                return 0;

            default:
                Console.Error.WriteLine($"Unknown Stage 0 command: {cmd}");
                return 2;
        }
    }

    public static string BuildStatusReport(AppLanguage lang = AppLanguage.En)
    {
        bool ru = lang == AppLanguage.Ru;
        var audio = new CoreAudioVolumeService().TryGetEndpointInfo();
        var cols = new K11HidMonitor().EnumerateCollections();

        var lines = new List<string>
        {
            ru
                ? "=== FiiO K11 R2R — Диагностика Stage 0 ==="
                : "=== FiiO K11 R2R Stage 0 Passive Status ==="
        };

        if (audio is null)
        {
            lines.Add(ru
                ? "Аудиовыход CoreAudio : Не найден (ЦАП отключён или в режиме прошивки)"
                : "CoreAudio Endpoint  : Not detected (DAC disconnected or in bootloader mode)");
        }
        else
        {
            lines.Add(ru
                ? $"Аудиовыход CoreAudio : {audio.FriendlyName} ({audio.InterfaceName})"
                : $"CoreAudio Endpoint  : {audio.FriendlyName} ({audio.InterfaceName})");
            lines.Add(ru
                ? $"  Диапазон / Шаг     : {audio.MinDb}..{audio.MaxDb} dB (шаг {audio.StepDb} dB)"
                : $"  Range / Step      : {audio.MinDb}..{audio.MaxDb} dB (step {audio.StepDb} dB)");
            string mutedStr = ru ? (audio.Muted ? "Да" : "Нет") : audio.Muted.ToString();
            lines.Add(ru
                ? $"  Громкость / Mute   : {audio.MasterDb} dB ({audio.MasterScalar:P0}), Mute={mutedStr}"
                : $"  Master / Mute     : {audio.MasterDb} dB ({audio.MasterScalar:P0}), Muted={mutedStr}");
            lines.Add(ru
                ? $"  Каналы (Л / П)     : {audio.Ch0Db} dB / {audio.Ch1Db} dB"
                : $"  Channels (L / R)  : {audio.Ch0Db} dB / {audio.Ch1Db} dB");
        }

        if (cols.Count == 0)
        {
            lines.Add(ru
                ? "HID-интерфейсы      : Нет (VID_2972&PID_0099 не обнаружен)"
                : "HID Collections     : None (VID_2972&PID_0099 not present)");
        }
        else
        {
            foreach (var c in cols)
            {
                lines.Add(ru
                    ? $"HID-интерфейс       : UP=0x{c.UsagePage:X4} U=0x{c.Usage:X4} In={c.InputReportByteLength} Out={c.OutputReportByteLength} Feat={c.FeatureReportByteLength}"
                    : $"HID Collection      : UP=0x{c.UsagePage:X4} U=0x{c.Usage:X4} In={c.InputReportByteLength} Out={c.OutputReportByteLength} Feat={c.FeatureReportByteLength}");
            }
        }

        lines.Add(string.Empty);
        lines.Add(ru
            ? "Примечание: на рабочей прошивке full экран не мигает тестовыми кодами; hex-вывод пакетов I2C2/USB на LCD работает только на прошивке stage0-diag."
            : "Note: production 'full' firmware stays silent on LCD; Stage 0 hex packet popups require 'stage0-diag' firmware.");

        return string.Join(Environment.NewLine, lines);
    }
}
