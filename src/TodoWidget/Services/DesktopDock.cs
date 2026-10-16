using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TodoWidget.Interop;

namespace TodoWidget.Services;

/// <summary>
/// Owns the Win32 window plumbing the widget needs: taskbar/Alt+Tab hiding,
/// Windows 11 rounded corners, bottom-of-Z-order "desktop" behaviour, and the
/// real WorkerW wallpaper-layer mount.
///
/// Geometry deliberately stays with WPF (<see cref="Window.Left"/> etc., in
/// device independent units). Driving the same HWND with raw physical-pixel
/// SetWindowPos calls fights WPF's own Width/Height and desynchronises on
/// scaled displays, so this class never positions the window except when it
/// has to translate between coordinate spaces during a re-parent.
/// </summary>
public sealed class DesktopDock : IDisposable
{
    private readonly Window _window;
    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _workerW = IntPtr.Zero;
    private HwndSource? _source;
    private bool _attached;
    private bool _disposed;

    public DesktopDock(Window window) => _window = window;

    public DockMode Mode { get; private set; } = DockMode.BottomMost;

    /// <summary>True while the 锁定 switch is on: the widget is pinned above every other window.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>True while the window is a child of Explorer's WorkerW.</summary>
    public bool IsDesktopLayer => _workerW != IntPtr.Zero;

    public void Attach()
    {
        if (_attached || _disposed) return;

        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("窗口句柄尚未创建。");

        NativeMethods.MakeToolWindow(_hwnd);
        NativeMethods.EnableRoundedCorners(_hwnd);

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        _attached = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            Unmount();
        }
        catch
        {
            // Teardown must never throw during application shutdown.
        }

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        _attached = false;
    }

    /// <summary>Applies <paramref name="mode"/>; returns false when the desktop layer was unavailable.</summary>
    public bool ApplyMode(DockMode mode)
    {
        Mode = mode;

        // 锁定 outranks every dock mode; the mode is (re)applied when unlocking.
        if (IsLocked) return true;

        switch (mode)
        {
            case DockMode.DesktopLayer:
                if (Mount()) return true;

                // Explorer handed us no usable desktop layer — fall back rather than vanish.
                Mode = DockMode.BottomMost;
                PushToBottom();
                return false;

            case DockMode.BottomMost:
                Unmount();
                PushToBottom();
                return true;

            case DockMode.Floating:
            default:
                Unmount();
                return true;
        }
    }

    /// <summary>
    /// 锁定 / 解锁. Locked pins the widget above all windows; unlocked restores the
    /// configured dock behaviour.
    ///
    /// Deliberately not a no-op when the state already matches: WPF re-applies its
    /// own z-order while the window is being shown, which silently drops a topmost
    /// request made during SourceInitialized. The view calls this again once the
    /// window is fully loaded so the pin actually sticks.
    /// </summary>
    public void SetLocked(bool locked)
    {
        IsLocked = locked;

        // A WorkerW child window can never be topmost, so leave the desktop layer
        // before asking for the top of the Z order.
        if (locked) Unmount();

        _window.Topmost = locked;

        if (locked)
        {
            PushToTop();
        }
        else
        {
            ApplyMode(Mode);
        }
    }

    private void PushToTop()
    {
        if (!_attached || _hwnd == IntPtr.Zero) return;

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>Re-applies the current mode; used after Explorer restarts.</summary>
    public void Refresh()
    {
        if (_disposed || IsLocked || Mode != DockMode.DesktopLayer) return;
        if (_workerW != IntPtr.Zero && NativeMethods.IsWindow(_workerW)) return;

        _workerW = IntPtr.Zero;
        if (!Mount()) Mode = DockMode.BottomMost;
    }

    private void PushToBottom()
    {
        if (!_attached || _hwnd == IntPtr.Zero || IsDesktopLayer || IsLocked) return;

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_BOTTOM,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    // ------------------------------------------------------------- WorkerW

    private bool Mount()
    {
        if (IsDesktopLayer) return true;
        if (!_attached || _hwnd == IntPtr.Zero) return false;

        // Only the WorkerW that Explorer keeps behind the desktop icons is safe to
        // mount into. Re-parenting to Progman itself was measured to produce a live
        // but permanently invisible window, so it is deliberately NOT used as a
        // fallback: a failed mount must degrade to BottomMost, never to "gone".
        var target = FindDesktopWorkerW();
        if (target == IntPtr.Zero) return false;

        var dpi = VisualTreeHelper.GetDpi(_window);
        var left = _window.Left;
        var top = _window.Top;

        NativeMethods.SetParent(_hwnd, target);
        _workerW = target;

        // WorkerW spans the virtual desktop, so its client origin is normally the
        // virtual screen origin and screen coordinates stay valid. Compensate when
        // it is not, so the widget does not jump when it is mounted.
        var origin = new POINT { X = 0, Y = 0 };
        if (NativeMethods.ClientToScreen(target, ref origin) && (origin.X != 0 || origin.Y != 0))
        {
            left -= origin.X / dpi.DpiScaleX;
            top -= origin.Y / dpi.DpiScaleY;
        }

        ReassertPlacement(left, top);
        return true;
    }

    private void Unmount()
    {
        if (!IsDesktopLayer) return;

        var dpi = VisualTreeHelper.GetDpi(_window);
        var left = _window.Left;
        var top = _window.Top;

        var origin = new POINT { X = 0, Y = 0 };
        var worker = _workerW;
        if (NativeMethods.ClientToScreen(worker, ref origin) && (origin.X != 0 || origin.Y != 0))
        {
            left += origin.X / dpi.DpiScaleX;
            top += origin.Y / dpi.DpiScaleY;
        }

        NativeMethods.SetParent(_hwnd, IntPtr.Zero);
        _workerW = IntPtr.Zero;

        // Re-parenting back to the desktop drops extended styles on some builds.
        NativeMethods.MakeToolWindow(_hwnd);
        NativeMethods.EnableRoundedCorners(_hwnd);

        ReassertPlacement(left, top);
    }

    /// <summary>Waits for WPF's layout pass before re-applying the position.</summary>
    private void ReassertPlacement(double left, double top)
    {
        if (double.IsNaN(left) || double.IsNaN(top)) return;

        _window.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (_disposed) return;
                _window.Left = left;
                _window.Top = top;
            }),
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Classic "wallpaper layer" trick: poke Progman so it spawns a WorkerW,
    /// then take the WorkerW sitting behind the desktop icons.
    /// </summary>
    private static IntPtr FindDesktopWorkerW()
    {
        var progman = NativeMethods.FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        NativeMethods.SendMessageTimeout(
            progman,
            NativeMethods.WM_SPAWN_WORKER,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.SMTO_NORMAL,
            1000,
            out _);

        var worker = IntPtr.Zero;
        NativeMethods.EnumWindows(
            (hwnd, _) =>
            {
                if (NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                {
                    worker = NativeMethods.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                }

                return true;
            },
            IntPtr.Zero);

        return worker;
    }

    // ------------------------------------------------------------- hook

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_WINDOWPOSCHANGING
            && Mode == DockMode.BottomMost
            && !IsDesktopLayer
            && !IsLocked)
        {
            // Resolve every z-order change — including the ones Windows starts when
            // the user clicks the widget — to the bottom of the stack.
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if (wp.hwnd == hwnd && wp.hwndInsertAfter != NativeMethods.HWND_BOTTOM)
            {
                wp.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
                wp.flags &= ~NativeMethods.SWP_NOZORDER;
                Marshal.StructureToPtr(wp, lParam, fDeleteOld: false);
            }
        }

        return IntPtr.Zero;
    }
}
