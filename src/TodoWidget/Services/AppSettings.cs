namespace TodoWidget.Services;

/// <summary>How the widget behaves relative to other windows.</summary>
public enum DockMode
{
    /// <summary>Every other window covers it; "show desktop" hides it. The default.</summary>
    BottomMost,

    /// <summary>Re-parented into Explorer's WorkerW, i.e.真正贴在壁纸层（Rainmeter 那种）。</summary>
    DesktopLayer,

    /// <summary>An ordinary floating window that can be covered and focused normally.</summary>
    Floating,
}

/// <summary>Persisted user preferences (window geometry + toggles).</summary>
public sealed class AppSettings
{
    /// <summary>Window left edge in device independent units; null means "no saved position yet".</summary>
    public double? Left { get; set; }

    public double? Top { get; set; }

    public double Width { get; set; } = 420;

    public double Height { get; set; } = 470;

    public DockMode Dock { get; set; } = DockMode.BottomMost;

    /// <summary>
    /// 锁定开关：true 时窗口置顶常驻（黑色）。解锁后回落到 <see cref="Dock"/> 描述的行为。
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>
    /// 折叠成小标签。只影响显示，不改变 <see cref="Width"/>/<see cref="Height"/>——
    /// 那两个始终保存展开后的尺寸，否则折叠状态下退出会把小标签的尺寸记成"用户想要的尺寸"，
    /// 下次启动就只剩一条。
    /// </summary>
    public bool Collapsed { get; set; }

    public bool AutoStart { get; set; }

    /// <summary>Show a tray balloon once when a task hits its deadline and once when it goes overdue.</summary>
    public bool NotifyOnDue { get; set; } = true;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
