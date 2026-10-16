using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TodoWidget.Interop;
using TodoWidget.Models;
using TodoWidget.Services;
using TodoWidget.ViewModels;

namespace TodoWidget.Views;

/// <summary>
/// The desktop widget itself. Also implements <see cref="ITaskEditor"/> so the
/// view-model can drive dialogs without knowing about WPF windows.
/// </summary>
public partial class MainWindow : Window, ITaskEditor
{
    private const double DefaultWidth = 420;
    private const double DefaultHeight = 470;
    private const double EdgeMargin = 24;

    /// <summary>尺寸下限只在展开态有意义；折叠成小标签时必须放开，否则窗口收不下去。</summary>
    private const double ExpandedMinWidth = 330;
    private const double ExpandedMinHeight = 240;

    /// <summary>小标签宽度：10(边距) + 18(方块) + 2 + 8(颜色点) + 7 + "待完成 NN" + 余量。</summary>
    private const double CollapsedWidth = 108;

    /// <summary>
    /// 40 = 38(标题栏高度) + 2(1px 边框×2)。让折叠态的标题栏高度与展开态完全相同，
    /// 黑方块在两种状态下落在同一个像素上，折叠时不会位移。
    /// </summary>
    private const double CollapsedHeight = 40;

    /// <summary>Physical pixels the cursor must travel before a header press becomes a drag.</summary>
    private const int DragThresholdPixels = 4;

    private readonly AppSettings _settings;
    private readonly DesktopDock _dock;
    private readonly DispatcherTimer _dockWatchdog;

    private bool _dragging;
    private bool _dragActive;
    private POINT _dragOrigin;

    /// <summary>这次按下是不是从黑方块开始的——决定松手时要不要切换折叠。</summary>
    private bool _pressStartedOnDot;

    private bool _resizing;
    private POINT _resizeOrigin;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    // 只由 RestoreGeometry 和 Grip_DragCompleted 维护——绝不能从折叠态或过渡态取值，
    // 否则会把小标签的尺寸当成用户偏好记下来。
    private double _expandedWidth = DefaultWidth;
    private double _expandedHeight = DefaultHeight;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;

        InitializeComponent();

        // 两个根节点的可见性由代码控制，先摆好初始状态，OnSourceInitialized 再按设置落实。
        CollapsedRoot.Visibility = Visibility.Collapsed;
        ExpandedRoot.Visibility = Visibility.Visible;

        ViewModel = new MainViewModel(settings, this);
        DataContext = ViewModel;

        _dock = new DesktopDock(this);

        // The view-model owns the 锁定 state; the dock owns the Z-order effect.
        ViewModel.LockChanged += (_, locked) => _dock.SetLocked(locked);

        // 折叠是窗口尺寸的变化，属于视图职责，所以由这里响应（并播动画）。
        ViewModel.CollapseChanged += (_, collapsed) => OnCollapseChanged(collapsed);

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;

        // Explorer restarts destroy the WorkerW we were mounted into; poll for it
        // (a 5 s IsWindow check costs nothing) and re-mount when needed.
        _dockWatchdog = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _dockWatchdog.Tick += (_, _) => _dock.Refresh();
        _dockWatchdog.Start();
    }

    public MainViewModel ViewModel { get; }

    // ------------------------------------------------------------- lifecycle

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _dock.Attach();
        RestoreGeometry();
        _dock.ApplyMode(_settings.Dock);

        // 折叠要在尺寸恢复之后应用，否则会被 RestoreGeometry 覆盖回展开尺寸。
        if (_settings.Collapsed) ApplyCollapsed(true);

        // 锁定 outranks the dock mode, so it is applied last.
        if (_settings.Locked) _dock.SetLocked(true);
    }

    /// <summary>
    /// WPF re-applies its own z-order while a window is being shown, which drops the
    /// topmost flag set during <see cref="OnSourceInitialized"/>. Re-assert it once
    /// the window is actually on screen.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_settings.Locked) _dock.SetLocked(true);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (App.IsShuttingDown)
        {
            SaveGeometry();
            _dockWatchdog.Stop();
            _dock.Dispose();
            return;
        }

        // The ✕ button hides the widget; the tray menu owns the real exit.
        e.Cancel = true;
        SaveGeometry();
        Hide();
    }

    private void RestoreGeometry()
    {
        // Everything here is in WPF device independent units, which is what
        // Window.Left/Top/Width/Height speak — no manual DPI maths required.
        Width = _settings.Width is > 200 and < 4000 ? _settings.Width : DefaultWidth;
        Height = _settings.Height is > 150 and < 4000 ? _settings.Height : DefaultHeight;
        _expandedWidth = Width;
        _expandedHeight = Height;

        var hasSavedPosition = _settings.Left is not null && _settings.Top is not null;

        if (hasSavedPosition)
        {
            Left = _settings.Left!.Value;
            Top = _settings.Top!.Value;
        }

        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        if (!hasSavedPosition || !virtualScreen.IntersectsWith(new Rect(Left, Top, Width, Height)))
        {
            // First run, or the saved monitor is gone: tuck into the bottom-right
            // of the primary work area, clamped so a taskbar or a small work area
            // can never push the widget off screen.
            var work = SystemParameters.WorkArea;
            Left = Math.Clamp(work.Right - Width - EdgeMargin, work.Left, Math.Max(work.Left, work.Right - Width));
            Top = Math.Clamp(work.Bottom - Height - EdgeMargin, work.Top, Math.Max(work.Top, work.Bottom - Height));
        }
    }

    public void SaveGeometry()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        // 折叠时窗口只有 128×32，不能把这个尺寸当成用户的偏好存下来。
        _settings.Width = _expandedWidth;
        _settings.Height = _expandedHeight;
        TaskStore.SaveSettings(_settings);
    }

    // ------------------------------------------------------------- 折叠 / 展开

    private void OnCollapseChanged(bool collapsed) => ApplyCollapsed(collapsed);

    /// <summary>
    /// 收起 / 展开，瞬时切换，不做任何动画。
    ///
    /// 唯一要保证的是**黑方块不许动**：折叠态的标题栏高度与展开态完全相同
    /// （见 <see cref="CollapsedHeight"/>），标签内容也按同样的左边距左对齐，
    /// 所以两种状态下方块都落在 window-local (11,11)–(29,29)。
    /// 窗口左上角不动，于是方块在屏幕上也待在同一个像素。
    /// </summary>
    private void ApplyCollapsed(bool collapsed)
    {
        _settings.Collapsed = collapsed;

        // 尺寸下限只在展开态有意义：MinWidth=330 时窗口根本收不到 108 宽。
        // 必须先放开下限再赋尺寸，否则 WPF 会把折叠尺寸夹到 330×240。
        MinWidth = 0;
        MinHeight = 0;

        Width = collapsed ? CollapsedWidth : _expandedWidth;
        Height = collapsed ? CollapsedHeight : _expandedHeight;

        CollapsedRoot.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        ExpandedRoot.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

        if (!collapsed)
        {
            MinWidth = ExpandedMinWidth;
            MinHeight = ExpandedMinHeight;
        }

        TaskStore.SaveSettings(_settings);
    }

    /// <summary>Returns false when the requested desktop-layer mount was unavailable.</summary>
    public bool ApplyDockMode(DockMode mode)
    {
        _settings.Dock = mode;
        return _dock.ApplyMode(mode);
    }

    /// <summary>Re-applies the dock behaviour plus the 锁定 pin (e.g. after showing from the tray).</summary>
    public void ReassertDock()
    {
        _dock.ApplyMode(_settings.Dock);
        if (_settings.Locked) _dock.SetLocked(true);
    }

    // ------------------------------------------------------------- chrome

    private void HideToTray_Click(object sender, RoutedEventArgs e)
    {
        SaveGeometry();
        Hide();
    }

    // ------------------------------------------------------------- Bug反馈与作者

    /// <summary>Two-step on purpose: the header text only opens the popup, so a stray click never launches a browser.</summary>
    private void AuthorLink_Click(object sender, RoutedEventArgs e)
    {
        AuthorPopup.IsOpen = !AuthorPopup.IsOpen;
        e.Handled = true;
    }

    private void Github_Click(object sender, RoutedEventArgs e)
    {
        AuthorPopup.IsOpen = false;
        OpenInBrowser(MainViewModel.AuthorHomepageUrl);
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Opening the default browser is a convenience, never a requirement.
        }
    }

    private void ClearCurrentPage_Click(object sender, RoutedEventArgs e)
    {
        switch (ViewModel.CurrentPage)
        {
            case PageKind.Done:
                ViewModel.ClearDoneCommand.Execute(null);
                break;
            case PageKind.Overdue:
                ViewModel.ClearOverdueCommand.Execute(null);
                break;
            default:
                // 待完成页面不提供一键清空，避免误删。
                break;
        }
    }

    // ------------------------------------------------------------- dragging

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (IsWithinButton(e.OriginalSource as DependencyObject)) return;

        // 黑方块不吞事件，所以按住它和按住标题栏走的是同一条路：
        // 没移动 = 一次点击（切换折叠）；移动了 = 拖窗口。
        _pressStartedOnDot = IsWithinCollapseDot(e.OriginalSource as DependencyObject);

        NativeMethods.GetCursorPos(out _dragOrigin);
        _dragging = true;
        _dragActive = false;

        // Capture on the Window, not the header: once the widget moves under the
        // cursor the header may no longer be the hit-test target, and a capture
        // held by a child element stops receiving the moves.
        CaptureMouse();
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }

        // The cursor is read in physical pixels, so convert the delta into the
        // device independent units Window.Left/Top use on this monitor.
        NativeMethods.GetCursorPos(out var now);
        var dx = now.X - _dragOrigin.X;
        var dy = now.Y - _dragOrigin.Y;

        if (!_dragActive)
        {
            // A plain click on the title bar must not nudge the widget.
            if (Math.Abs(dx) < DragThresholdPixels && Math.Abs(dy) < DragThresholdPixels) return;

            _dragActive = true;
            _dragOrigin = now;   // re-anchor so crossing the threshold causes no jump
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        Left += dx / dpi.DpiScaleX;
        Top += dy / dpi.DpiScaleY;

        // Re-anchor so accumulated rounding cannot drift during a long drag.
        _dragOrigin = now;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (!_dragging) return;

        var moved = _dragActive;
        var wasDot = _pressStartedOnDot;

        _dragging = false;
        _dragActive = false;
        _pressStartedOnDot = false;
        ReleaseMouseCapture();

        if (moved)
        {
            SaveGeometry();
            return;
        }

        // 按着没动 = 一次点击：只有黑方块有动作，标题栏其它地方什么都不做。
        if (wasDot) ViewModel.SetCollapsed(!ViewModel.IsCollapsed);
    }

    private static bool IsWithinButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is ButtonBase) return true;

            node = node switch
            {
                Visual => VisualTreeHelper.GetParent(node),
                _ => LogicalTreeHelper.GetParent(node),
            };
        }

        return false;
    }

    /// <summary>
    /// 黑方块在 XAML 里是 Border 而不是 Button（Button 会吞掉按下事件，长按拖动就没法做了），
    /// 所以用 Tag 标记来认它。
    /// </summary>
    private static bool IsWithinCollapseDot(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is FrameworkElement { Tag: "collapse-dot" }) return true;

            node = node switch
            {
                Visual => VisualTreeHelper.GetParent(node),
                _ => LogicalTreeHelper.GetParent(node),
            };
        }

        return false;
    }

    // ------------------------------------------------------------- resizing

    private void Grip_DragStarted(object sender, DragStartedEventArgs e)
    {
        NativeMethods.GetCursorPos(out _resizeOrigin);
        _resizeStartWidth = Width;
        _resizeStartHeight = Height;
        _resizing = true;
    }

    private void Grip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_resizing) return;

        // 从"按下的那一刻"算起，中途绝不重新锚定。
        // 重新锚定会把被最小尺寸夹掉的那段位移丢掉，表现为：拖到底再往回拖时
        // 窗口不跟手（要空拖一段才动）。用固定锚点则窗口角始终贴着光标，
        // 到最小值就停住，往回拖立刻跟上。
        var dpi = VisualTreeHelper.GetDpi(this);
        NativeMethods.GetCursorPos(out var now);

        Width = Math.Max(MinWidth, _resizeStartWidth + (now.X - _resizeOrigin.X) / dpi.DpiScaleX);
        Height = Math.Max(MinHeight, _resizeStartHeight + (now.Y - _resizeOrigin.Y) / dpi.DpiScaleY);
    }

    private void Grip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _resizing = false;

        // 缩放只在展开态可能发生；这里同步记住用户调出来的尺寸。
        _expandedWidth = Width;
        _expandedHeight = Height;

        SaveGeometry();
    }

    // ------------------------------------------------------------- ITaskEditor

    public TodoTask? CreateTask(DateTime defaultDue)
    {
        var draft = new TodoTask { Title = string.Empty, DueAt = defaultDue };
        var dialog = new TaskEditWindow(draft, isNew: true);
        return dialog.ShowDialog() == true ? draft : null;
    }

    public bool EditTask(TodoTask task)
    {
        // Edit a copy so cancelling really cancels.
        var draft = task.Clone();
        var dialog = new TaskEditWindow(draft, isNew: false);
        if (dialog.ShowDialog() != true) return false;

        task.Title = draft.Title;
        task.DueAt = draft.DueAt;
        return true;
    }

    public bool Confirm(string message) => ConfirmWindow.Ask(message);
}
