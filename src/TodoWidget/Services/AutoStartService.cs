using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace TodoWidget.Services;

/// <summary>
/// 开机自启动 via HKCU\...\Run —— 不需要管理员权限，也不用维护快捷方式文件。
///
/// 硬规则：**自启动只能由「安装目录里的那一份」来管理。**
///
/// 早先的版本是谁运行谁写，于是调试构建（src\...\bin\Debug\TodoWidget.exe）把自己
/// 写进了 Run 值：开机起来的是开发构建，正式版的开机启动被静默顶掉了。
/// 现在用两道闸门堵死：
///
///   1. <b>DEBUG 构建完全不碰注册表</b>（见 <see cref="IsAvailable"/>）。
///      调试时无论点没点托盘那一项，都不可能改到正式版的自启动。
///   2. <b>安装归属标记</b>（见 <see cref="StateKeyPath"/>）：安装器写入 InstallDir。
///      标记存在时，只有从该目录运行的实例才被允许读写自启动；
///      便携副本、随手复制的第二份都只能读、不能改。
///
/// 没有安装标记时放行——那种情况是纯便携使用，本来就没有「正式版」可抢。
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TodoWidget";

    /// <summary>本程序自己的状态键，与 Run 分开，方便卸载时整体删除。</summary>
    private const string StateKeyPath = @"Software\TodoWidget";
    private const string InstallDirValueName = "InstallDir";

    public static string ExecutablePath =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? throw new InvalidOperationException("无法解析当前可执行文件路径。");

#if DEBUG
    /// <summary>开发构建永远不碰自启动注册表——这样调试时不可能顶掉正式版的开机启动。</summary>
    public static bool IsAvailable => false;

    public const string UnavailableReason = "开发构建不参与开机自启动";
#else
    /// <summary>是否允许当前这份可执行文件改写自启动。</summary>
    public static bool IsAvailable => IsManagedHere();

    public const string UnavailableReason = "只有安装目录里的那份才能管理开机自启动";
#endif

    /// <summary>安装器写下的安装目录；纯便携使用时为 null。</summary>
    private static string? InstallDirectory()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StateKeyPath, writable: false);
            return key?.GetValue(InstallDirValueName) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 当前可执行文件是不是「安装目录里的那一份」。
    /// 没有标记、或标记指向的目录已经不存在时返回 true —— 宁可放开便携使用，
    /// 也不要因为一个陈旧的标记让自启动彻底失效。
    /// </summary>
    private static bool IsManagedHere()
    {
        var installDir = InstallDirectory();
        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) return true;

        var runningDir = Path.GetDirectoryName(ExecutablePath);
        if (string.IsNullOrEmpty(runningDir)) return false;

        return string.Equals(Normalize(runningDir), Normalize(installDir), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            return path.Trim().TrimEnd('\\');
        }
    }

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
        if (!IsAvailable) return false;

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
        if (!IsAvailable) return;

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
