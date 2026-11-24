using System.Runtime.Versioning;
using Microsoft.Win32;

namespace K11Control.Config;

[SupportedOSPlatform("windows")]
public static class AutostartManager
{
    public const string RunRegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "FiiOK11R2RControl";

    public static string? FormatRunCommand(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return null;
        string trimmed = exePath.Trim().Trim('"');
        if (!trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return null;
        return $"\"{trimmed}\"";
    }

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: false);
            string? val = key?.GetValue(RunValueName) as string;
            return !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public static bool TrySetEnabled(bool enabled, out string? error, string? exePathOverride = null)
    {
        error = null;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunRegistryKeyPath, writable: true);
            if (key is null)
            {
                error = "Failed to open HKCU Run registry key.";
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
                return true;
            }

            string? path = exePathOverride ?? Environment.ProcessPath;
            string? cmd = FormatRunCommand(path);
            if (cmd is null)
            {
                error = "Cannot register autostart: executable path is not a .exe.";
                return false;
            }

            key.SetValue(RunValueName, cmd, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
