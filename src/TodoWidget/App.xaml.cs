using System.Diagnostics;
using System.Threading;
using System.Windows;
using TodoWidget.Services;
using TodoWidget.Views;

namespace TodoWidget;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\TodoWidget.SingleInstance";
    private const string WindowTitle = "待办清单";
    private const int SwShow = 5;
    private const int SwRestore = 9;

    private Mutex? _mutex;
    private TrayService? _tray;
    private MainWindow? _window;
    private AppSettings _settings = new();

    /// <summary>Distinguishes "hide to tray" (cancel the close) from a real quit.</summary>
    public static bool IsShuttingDown { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            BringExistingInstanceForward();
            Shutdown();
            return;
        }

        // The tray icon keeps the process alive, so hiding the window must not quit.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settings = TaskStore.LoadSettings();

        // The registry is the source of truth for autostart, not our own file.
        _settings.AutoStart = AutoStartService.IsEnabled();
        AutoStartService.RepairPathIfEnabled();

        _window = new MainWindow(_settings);
        _window.Show();

        _tray = new TrayService();
        WireTray();
        _tray.SyncState(_settings);

        _window.ViewModel.NotificationRequested += (_, notice) =>
            _tray.ShowBalloon(notice.Title, notice.Message);

        // The 锁定 switch lives on the widget and is mirrored in the tray menu;
        // whoever flips it, the state is persisted here.
        _window.ViewModel.LockChanged += (_, locked) =>
        {
            _settings.Locked = locked;
            TaskStore.SaveSettings(_settings);
            _tray?.SyncState(_settings);
        };

        // 折叠状态由 MainWindow 负责落盘（它同时要改窗口尺寸），这里只同步托盘勾选。
        _window.ViewModel.CollapseChanged += (_, _) => _tray?.SyncState(_settings);

        _window.ViewModel.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _window?.ViewModel.Stop();
        _tray?.Dispose();
        _tray = null;

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not the owner (the second instance that lost the race).
        }

        _mutex?.Dispose();
        _mutex = null;

        base.OnExit(e);
    }

    private void WireTray()
    {
        if (_tray is null || _window is null) return;

        _tray.ToggleVisibilityRequested += (_, _) => ToggleWindow();
        _tray.LockRequested += (_, locked) => _window.ViewModel.SetLocked(locked);
        _tray.CollapseRequested += (_, collapsed) => _window.ViewModel.SetCollapsed(collapsed);
        _tray.RefreshRequested += (_, _) => _window.ViewModel.Refresh(force: true);
        _tray.AddRequested += (_, _) =>
        {
            ShowWindow();
            _window.ViewModel.AddCommand.Execute(null);
        };

        _tray.DockModeRequested += (_, mode) =>
        {
            if (!_window.ApplyDockMode(mode))
            {
                // Never leave a mode that silently produced no visible window.
                _window.ApplyDockMode(DockMode.BottomMost);
                _tray.ShowBalloon(
                    "无法钉到桌面层",
                    "当前 Windows 桌面结构不支持壁纸层挂载，已回退为「贴底小部件」。");
            }

            TaskStore.SaveSettings(_settings);
            _tray.SyncState(_settings);
        };

        _tray.AutoStartRequested += (_, enabled) =>
        {
            var applied = AutoStartService.Set(enabled);
            _settings.AutoStart = applied ? AutoStartService.IsEnabled() : _settings.AutoStart;
            TaskStore.SaveSettings(_settings);
            _tray.SyncState(_settings);
        };

        _tray.NotifyRequested += (_, enabled) =>
        {
            _settings.NotifyOnDue = enabled;
            TaskStore.SaveSettings(_settings);
            _tray.SyncState(_settings);
        };

        _tray.OpenDataFolderRequested += (_, _) => OpenDataFolder();
        _tray.ExitRequested += (_, _) => Quit();
    }

    private void ToggleWindow()
    {
        if (_window is null) return;
        if (_window.IsVisible && _window.WindowState != WindowState.Minimized) _window.Hide();
        else ShowWindow();
    }

    private void ShowWindow()
    {
        if (_window is null) return;

        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.ReassertDock();
    }

    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.BaseDir,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Opening Explorer is a convenience, never a requirement.
        }
    }

    public void Quit()
    {
        IsShuttingDown = true;
        _window?.SaveGeometry();
        _window?.Close();
        _window = null;

        _tray?.Dispose();
        _tray = null;

        Shutdown();
    }

    /// <summary>Focuses a widget that is already running instead of starting a second copy.</summary>
    private static void BringExistingInstanceForward()
    {
        var hwnd = Interop.NativeMethods.FindWindow(null, WindowTitle);
        if (hwnd == IntPtr.Zero) return;

        Interop.NativeMethods.ShowWindow(hwnd, SwRestore);
        Interop.NativeMethods.ShowWindow(hwnd, SwShow);
        Interop.NativeMethods.SetForegroundWindow(hwnd);
    }
}
