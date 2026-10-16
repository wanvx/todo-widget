using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace TodoWidget.Services;

/// <summary>
/// 开机自启动 via HKCU\...\Run — no administrator rights and no shortcut file to manage.
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TodoWidget";

    public static string ExecutablePath =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? throw new InvalidOperationException("无法解析当前可执行文件路径。");

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return false;

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExecutablePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Keeps the registry in sync with the running executable after the app has been
    /// moved to a new folder, without ever turning autostart on by itself.
    /// </summary>
    public static void RepairPathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not string existing || existing.Length == 0) return;

            var expected = $"\"{ExecutablePath}\"";
            if (string.Equals(existing, expected, StringComparison.OrdinalIgnoreCase)) return;

            // Only take over once the recorded executable is actually gone. Rewriting
            // unconditionally let a differently located build (a debug run, a second
            // copy) silently hijack a perfectly valid autostart entry.
            var recorded = existing.Trim().Trim('"');
            if (recorded.Length > 0 && File.Exists(recorded)) return;

            key.SetValue(ValueName, expected, RegistryValueKind.String);
        }
        catch
        {
        }
    }
}
