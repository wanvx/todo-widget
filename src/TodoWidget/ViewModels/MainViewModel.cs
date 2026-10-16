using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using TodoWidget.Models;
using TodoWidget.Services;

namespace TodoWidget.ViewModels;

/// <summary>
/// Owns the task list and the 30 second clock. The page a task belongs to is
/// always recomputed from the wall clock, never stored, so a task can never be
/// stranded on the wrong page.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly List<TodoTask> _tasks;
    private readonly AppSettings _settings;
    private readonly ITaskEditor _editor;
    private readonly HashSet<string> _notifiedDue = new(StringComparer.Ordinal);
    private readonly HashSet<string> _notifiedOverdue = new(StringComparer.Ordinal);

    private DispatcherTimer? _ticker;
    private DateTime _now = DateTime.Now;
    private PageKind _currentPage = PageKind.Active;
    private bool _primed;
    private bool _isLocked;
    private bool _isCollapsed;
    private string _lastRefreshText = string.Empty;

    public MainViewModel(AppSettings settings, ITaskEditor editor)
    {
        _settings = settings;
        _editor = editor;
        _tasks = TaskStore.Load();
        _isLocked = settings.Locked;
        _isCollapsed = settings.Collapsed;

        ActiveTasks = new ObservableCollection<TaskItemViewModel>();
        DoneTasks = new ObservableCollection<TaskItemViewModel>();
        OverdueTasks = new ObservableCollection<TaskItemViewModel>();

        AddCommand = new RelayCommand(_ => AddTask());
        RefreshCommand = new RelayCommand(_ => Refresh(force: true));
        ClearDoneCommand = new RelayCommand(_ => ClearDone());
        ClearOverdueCommand = new RelayCommand(_ => ClearOverdue());
        ShowActiveCommand = new RelayCommand(_ => CurrentPage = PageKind.Active);
        ShowDoneCommand = new RelayCommand(_ => CurrentPage = PageKind.Done);
        ShowOverdueCommand = new RelayCommand(_ => CurrentPage = PageKind.Overdue);
        ToggleLockCommand = new RelayCommand(_ => SetLocked(!IsLocked));
        ToggleCollapseCommand = new RelayCommand(_ => SetCollapsed(!IsCollapsed));
    }

    /// <summary>Raised when a deadline (or the 24 h overdue mark) is crossed.</summary>
    public event EventHandler<(string Title, string Message)>? NotificationRequested;

    /// <summary>Raised when the 锁定 switch is flipped, with the new state.</summary>
    public event EventHandler<bool>? LockChanged;

    /// <summary>Raised when the widget is collapsed / expanded; the view resizes the window.</summary>
    public event EventHandler<bool>? CollapseChanged;

    public ObservableCollection<TaskItemViewModel> ActiveTasks { get; }

    public ObservableCollection<TaskItemViewModel> DoneTasks { get; }

    public ObservableCollection<TaskItemViewModel> OverdueTasks { get; }

    public RelayCommand AddCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ClearDoneCommand { get; }

    public RelayCommand ClearOverdueCommand { get; }

    public RelayCommand ShowActiveCommand { get; }

    public RelayCommand ShowDoneCommand { get; }

    public RelayCommand ShowOverdueCommand { get; }

    public RelayCommand ToggleLockCommand { get; }

    public RelayCommand ToggleCollapseCommand { get; }

    /// <summary>True while the widget is shrunk to the little 待完成 tag.</summary>
    public bool IsCollapsed => _isCollapsed;

    public string CollapseTooltip => IsCollapsed
        ? "点击展开清单（长按可拖动窗口）"
        : "点击折叠成小标签（长按可拖动窗口）";

    /// <summary>
    /// 折叠标签上那个小圆点的颜色，取最紧急的待办（列表已按截止时间升序，
    /// 所以第一条就是最紧急的）；没有待办时是灰色。
    /// </summary>
    public Brush MostUrgentBrush =>
        ActiveTasks.Count > 0 ? ActiveTasks[0].SquareBrush : UrgencyPalette.Muted;

    /// <summary>Single entry point for both the on-widget dot and the tray menu.</summary>
    public void SetCollapsed(bool collapsed)
    {
        if (_isCollapsed == collapsed) return;

        _isCollapsed = collapsed;
        OnPropertyChanged(nameof(IsCollapsed));
        OnPropertyChanged(nameof(CollapseTooltip));
        CollapseChanged?.Invoke(this, collapsed);
    }

    /// <summary>Black switch on: the widget stays above every other window.</summary>
    public bool IsLocked => _isLocked;

    public string LockTooltip => IsLocked
        ? "已锁定：界面固定在最顶层，不会被其他窗口遮挡（点击解锁）"
        : "已解锁：只贴在桌面，会被其他窗口遮挡（点击锁定到最顶层）";

    /// <summary>Single entry point for both the on-widget switch and the tray menu.</summary>
    public void SetLocked(bool locked)
    {
        if (_isLocked == locked) return;

        _isLocked = locked;
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(LockTooltip));
        LockChanged?.Invoke(this, locked);
    }

    public PageKind CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            OnPropertyChanged(nameof(IsActivePage));
            OnPropertyChanged(nameof(IsDonePage));
            OnPropertyChanged(nameof(IsOverduePage));
        }
    }

    public bool IsActivePage => CurrentPage == PageKind.Active;

    public bool IsDonePage => CurrentPage == PageKind.Done;

    public bool IsOverduePage => CurrentPage == PageKind.Overdue;

    public int ActiveCount => ActiveTasks.Count;

    public int DoneCount => DoneTasks.Count;

    public int OverdueCount => OverdueTasks.Count;

    public bool IsActiveEmpty => ActiveTasks.Count == 0;

    public bool IsDoneEmpty => DoneTasks.Count == 0;

    public bool IsOverdueEmpty => OverdueTasks.Count == 0;

    public string LastRefreshText
    {
        get => _lastRefreshText;
        private set => SetProperty(ref _lastRefreshText, value);
    }

    public string DataFolder => AppPaths.BaseDir;

    public string DataFilePath => AppPaths.DataFile;

    /// <summary>
    /// 作者主页 / Bug 反馈地址。**要改链接只改这一行**——弹出框里的灰色提示文字
    /// 由 <see cref="AuthorHomepageLabel"/> 自动推导，不会两边不一致。
    /// 以后建好仓库想接到 Issues，把它换成 https://github.com/wanvx/&lt;仓库&gt;/issues 即可。
    /// </summary>
    public const string AuthorHomepageUrl = "https://github.com/wanvx";

    /// <summary>弹出框里显示的地址（去掉协议前缀，短一点更好看）。</summary>
    public string AuthorHomepageLabel => AuthorHomepageUrl.Replace("https://", string.Empty).TrimEnd('/');

    public void Start()
    {
        _ticker = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _ticker.Tick += (_, _) => Refresh();
        _ticker.Start();

        Refresh(force: true);
    }

    public void Stop()
    {
        _ticker?.Stop();
        _ticker = null;
    }

    /// <summary>
    /// Re-partitions every task against the current clock. When the partition is
    /// unchanged the existing rows are updated in place, which keeps the 30 s tick
    /// flicker-free and allocation-light.
    /// </summary>
    public void Refresh(bool force = false)
    {
        _now = DateTime.Now;

        var active = new List<TodoTask>();
        var done = new List<TodoTask>();
        var overdue = new List<TodoTask>();

        foreach (var task in _tasks)
        {
            switch (UrgencyRules.BucketOf(task, _now))
            {
                case TaskBucket.Done:
                    done.Add(task);
                    break;
                case TaskBucket.Overdue:
                    overdue.Add(task);
                    break;
                default:
                    active.Add(task);
                    break;
            }
        }

        active.Sort((a, b) => a.DueAt.CompareTo(b.DueAt));
        overdue.Sort((a, b) => a.DueAt.CompareTo(b.DueAt));
        done.Sort((a, b) => (b.CompletedAt ?? b.DueAt).CompareTo(a.CompletedAt ?? a.DueAt));

        Sync(ActiveTasks, active, force);
        Sync(DoneTasks, done, force);
        Sync(OverdueTasks, overdue, force);

        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(DoneCount));
        OnPropertyChanged(nameof(OverdueCount));
        OnPropertyChanged(nameof(IsActiveEmpty));
        OnPropertyChanged(nameof(IsDoneEmpty));
        OnPropertyChanged(nameof(IsOverdueEmpty));
        OnPropertyChanged(nameof(MostUrgentBrush));

        LastRefreshText = $"更新于 {_now:HH:mm:ss}";

        DetectNotifications(_now);
    }

    private void Sync(ObservableCollection<TaskItemViewModel> target, List<TodoTask> desired, bool force)
    {
        var sameShape = !force && target.Count == desired.Count;

        if (sameShape)
        {
            for (var i = 0; i < desired.Count; i++)
            {
                if (ReferenceEquals(target[i].Model, desired[i])) continue;
                sameShape = false;
                break;
            }
        }

        if (!sameShape)
        {
            target.Clear();
            foreach (var model in desired) target.Add(CreateItem(model));
        }

        foreach (var item in target) item.Refresh(_now);
    }

    private TaskItemViewModel CreateItem(TodoTask model) =>
        new(model, ToggleTask, EditTask, DeleteTask);

    // ------------------------------------------------------------- row actions

    private void ToggleTask(TaskItemViewModel item)
    {
        item.Model.IsDone = !item.Model.IsDone;
        item.Model.CompletedAt = item.Model.IsDone ? DateTime.Now : null;

        // 打勾后立即走一次刷新，把事项送进「已完成」页面。
        SaveAndRefresh();
    }

    private void EditTask(TaskItemViewModel item)
    {
        if (!_editor.EditTask(item.Model)) return;
        SaveAndRefresh();
    }

    private void DeleteTask(TaskItemViewModel item)
    {
        if (!_editor.Confirm($"确定删除「{item.Title}」？")) return;
        _tasks.RemoveAll(t => t.Id == item.Id);
        SaveAndRefresh();
    }

    private void AddTask()
    {
        var created = _editor.CreateTask(DefaultDue());
        if (created is null) return;

        if (string.IsNullOrWhiteSpace(created.Title)) return;
        created.DueAt = UrgencyRules.Normalize(created.DueAt);
        _tasks.Add(created);
        SaveAndRefresh();
    }

    private void ClearDone()
    {
        var count = DoneTasks.Count;
        if (count == 0) return;
        if (!_editor.Confirm($"确定清空 {count} 条已完成事项？")) return;

        _tasks.RemoveAll(t => t.IsDone);
        SaveAndRefresh();
    }

    private void ClearOverdue()
    {
        var count = OverdueTasks.Count;
        if (count == 0) return;
        if (!_editor.Confirm($"确定清空 {count} 条超时事项？")) return;

        var doomed = OverdueTasks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        _tasks.RemoveAll(t => doomed.Contains(t.Id));
        SaveAndRefresh();
    }

    private void SaveAndRefresh()
    {
        TaskStore.Save(_tasks);
        Refresh(force: true);
    }

    private static DateTime DefaultDue() =>
        UrgencyRules.Normalize(DateTime.Now.AddHours(24));

    // ------------------------------------------------------------- notification

    private void DetectNotifications(DateTime now)
    {
        if (!_settings.NotifyOnDue) return;

        if (!_primed)
        {
            // Do not fire a storm of balloons for everything that was already
            // overdue when the widget started.
            foreach (var task in _tasks)
            {
                if (task.IsDone) continue;
                if (now >= task.DueAt) _notifiedDue.Add(task.Id);
                if (now >= task.DueAt + UrgencyRules.OverdueGrace) _notifiedOverdue.Add(task.Id);
            }

            _primed = true;
            return;
        }

        foreach (var task in _tasks)
        {
            if (task.IsDone)
            {
                _notifiedDue.Remove(task.Id);
                _notifiedOverdue.Remove(task.Id);
                continue;
            }

            if (_notifiedDue.Add(task.Id) && now >= task.DueAt)
            {
                NotificationRequested?.Invoke(this, ($"任务到期：{task.Title}", $"截止时间 {task.DueAt:yyyy-MM-dd HH}"));
            }

            if (_notifiedOverdue.Add(task.Id) && now >= task.DueAt + UrgencyRules.OverdueGrace)
            {
                NotificationRequested?.Invoke(this, ($"任务已超时：{task.Title}", "超过截止时间 24 小时，已移入「超时」页面"));
            }
        }
    }
}
