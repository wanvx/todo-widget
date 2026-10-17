using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TodoWidget.Services;

namespace TodoWidget.Services;

/// <summary>
/// System tray presence. Uses WinForms' NotifyIcon because WPF has no built-in
/// tray API and this assembly already ships with the Windows Desktop runtime —
/// keeping the app free of NuGet dependencies.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _generatedIcon;
    private readonly IntPtr _hIcon;
    private ToolStripMenuItem? _autoStartItem;
    private ToolStripMenuItem? _notifyItem;
    private ToolStripMenuItem? _dockItems;
    private ToolStripMenuItem? _lockItem;
    private ToolStripMenuItem? _collapseItem;

    public TrayService()
    {
        _generatedIcon = CreateCheckIcon(out _hIcon);

        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
        };

        var header = new ToolStripMenuItem("待办清单") { Enabled = false };
        var add = new ToolStripMenuItem("添加任务…", null, (_, _) => AddRequested?.Invoke(this, EventArgs.Empty));
        var refresh = new ToolStripMenuItem("刷新", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        var toggle = new ToolStripMenuItem("显示 / 隐藏", null, (_, _) => ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty));

        var dockHeader = new ToolStripMenuItem("桌面停靠方式") { Enabled = false };
        _dockItems = dockHeader;
        foreach (var (label, mode) in new[]
                 {
                     ("贴底小部件（推荐）", DockMode.BottomMost),
                     ("真·桌面层（壁纸层，需系统支持）", DockMode.DesktopLayer),
                     ("普通窗口", DockMode.Floating),
                 })
        {
            var item = new ToolStripMenuItem(label) { Tag = mode };
            item.Click += (s, _) => DockModeRequested?.Invoke(this, (DockMode)((ToolStripMenuItem)s!).Tag!);
            dockHeader.DropDownItems.Add(item);
        }

        // Mirrors the black/white switch on the widget itself.
        _lockItem = new ToolStripMenuItem("锁定到最顶层") { CheckOnClick = true };
        _lockItem.Click += (s, _) => LockRequested?.Invoke(this, ((ToolStripMenuItem)s!).Checked);

        // 折叠后窗口只剩左上角一个小标签，如果被别的窗口盖住就点不到了，
        // 所以托盘里留一个同样的开关作为退路。
        _collapseItem = new ToolStripMenuItem("折叠为小标签") { CheckOnClick = true };
        _collapseItem.Click += (s, _) => CollapseRequested?.Invoke(this, ((ToolStripMenuItem)s!).Checked);

        _autoStartItem = new ToolStripMenuItem("开机自启动") { CheckOnClick = true };
        _autoStartItem.Click += (s, _) =>
        {
            var item = (ToolStripMenuItem)s!;
            AutoStartRequested?.Invoke(this, item.Checked);
        };

        // 开发构建、以及不是从安装目录运行的副本，无权改写自启动（见 AutoStartService）。
        // 置灰并把原因放进 tooltip，免得用户以为"点了没反应"。
        if (!AutoStartService.IsAvailable)
        {
            _autoStartItem.Enabled = false;
            _autoStartItem.CheckOnClick = false;
            _autoStartItem.ToolTipText = AutoStartService.UnavailableReason;
        }

        _notifyItem = new ToolStripMenuItem("到期气泡提醒") { CheckOnClick = true };
        _notifyItem.Click += (s, _) => NotifyRequested?.Invoke(this, ((ToolStripMenuItem)s!).Checked);

        var settingsFile = new ToolStripMenuItem("打开数据文件夹", null, (_, _) => OpenDataFolderRequested?.Invoke(this, EventArgs.Empty));

        var exit = new ToolStripMenuItem("退出", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        // 托盘菜单里的条目默认不显示 tooltip，这里打开，好在「开机自启动」置灰时说明原因。
        menu.ShowItemToolTips = true;

        menu.Items.AddRange(new ToolStripItem[]
        {
            header,
            new ToolStripSeparator(),
            add,
            refresh,
            toggle,
            new ToolStripSeparator(),
            _lockItem,
            _collapseItem,
            dockHeader,
            _autoStartItem,
            _notifyItem,
            settingsFile,
            new ToolStripSeparator(),
            exit,
        });

        _icon = new NotifyIcon
        {
            Icon = _generatedIcon,
            Text = "待办清单",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _icon.DoubleClick += (_, _) => ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? AddRequested;

    public event EventHandler? RefreshRequested;

    public event EventHandler? ToggleVisibilityRequested;

    public event EventHandler<DockMode>? DockModeRequested;

    public event EventHandler<bool>? AutoStartRequested;

    public event EventHandler<bool>? NotifyRequested;

    /// <summary>Raised with the new 锁定 state when the tray item is toggled.</summary>
    public event EventHandler<bool>? LockRequested;

    /// <summary>Raised with the new folded state when the tray item is toggled.</summary>
    public event EventHandler<bool>? CollapseRequested;

    public event EventHandler? OpenDataFolderRequested;

    public event EventHandler? ExitRequested;

    public void SyncState(AppSettings settings)
    {
        if (_autoStartItem is not null) _autoStartItem.Checked = settings.AutoStart;
        if (_notifyItem is not null) _notifyItem.Checked = settings.NotifyOnDue;
        if (_lockItem is not null) _lockItem.Checked = settings.Locked;
        if (_collapseItem is not null) _collapseItem.Checked = settings.Collapsed;

        if (_dockItems is not null)
        {
            foreach (ToolStripItem item in _dockItems.DropDownItems)
            {
                if (item is ToolStripMenuItem menuItem && menuItem.Tag is DockMode mode)
                {
                    menuItem.Checked = mode == settings.Dock;
                }
            }
        }
    }

    public void ShowBalloon(string title, string message)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = message;
            _icon.BalloonTipIcon = ToolTipIcon.Info;
            _icon.ShowBalloonTip(6000);
        }
        catch
        {
            // Notifications are best effort.
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();

        if (_hIcon != IntPtr.Zero)
        {
            Interop.NativeMethods.DestroyIcon(_hIcon);
        }

        _generatedIcon.Dispose();
    }

    /// <summary>
    /// Draws the tray glyph at runtime: a dark rounded square with a white check,
    /// readable on both light and dark taskbars, and requires no embedded asset.
    /// </summary>
    private static Icon CreateCheckIcon(out IntPtr handle)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var background = new SolidBrush(Color.FromArgb(255, 24, 24, 27));
            using var path = RoundedRect(new RectangleF(1.5f, 1.5f, 29f, 29f), 8f);
            g.FillPath(background, path);

            using var pen = new Pen(Color.White, 3.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            g.DrawLines(pen, new[]
            {
                new PointF(9f, 16.5f),
                new PointF(14f, 21.5f),
                new PointF(23f, 11f),
            });
        }

        handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
