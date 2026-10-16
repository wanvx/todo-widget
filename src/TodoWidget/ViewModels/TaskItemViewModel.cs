using System.Windows.Media;
using TodoWidget.Models;

namespace TodoWidget.ViewModels;

/// <summary>One rendered row: 【颜色方块】【任务】：【截止时间】【剩余时间】【打勾】.</summary>
public sealed class TaskItemViewModel : ObservableObject
{
    private readonly Action<TaskItemViewModel> _onToggle;
    private readonly Action<TaskItemViewModel> _onEdit;
    private readonly Action<TaskItemViewModel> _onDelete;

    private string _remainingText = string.Empty;
    private Urgency _urgency = Urgency.Green;

    public TaskItemViewModel(
        TodoTask model,
        Action<TaskItemViewModel> onToggle,
        Action<TaskItemViewModel> onEdit,
        Action<TaskItemViewModel> onDelete)
    {
        Model = model;
        _onToggle = onToggle;
        _onEdit = onEdit;
        _onDelete = onDelete;
    }

    public TodoTask Model { get; }

    public string Id => Model.Id;

    public string Title => Model.Title;

    public string DueText => Model.DueAt.ToString("yyyy-MM-dd HH");

    public string RemainingText
    {
        get => _remainingText;
        private set => SetProperty(ref _remainingText, value);
    }

    public Brush SquareBrush => Model.IsDone ? UrgencyPalette.Muted : UrgencyPalette.For(_urgency);

    public bool IsDone => Model.IsDone;

    /// <summary>
    /// The checkbox binds IsChecked OneWay to <see cref="IsDone"/> and drives this
    /// command instead of a TwoWay binding. A TwoWay binding makes WPF push the
    /// target's default (false) back into the source while the template is being
    /// realised, which silently un-completes tasks.
    /// </summary>
    public RelayCommand ToggleCommand => _toggleCommand ??= new RelayCommand(_ => _onToggle(this));
    private RelayCommand? _toggleCommand;

    public RelayCommand EditCommand => _editCommand ??= new RelayCommand(_ => _onEdit(this));
    private RelayCommand? _editCommand;

    public RelayCommand DeleteCommand => _deleteCommand ??= new RelayCommand(_ => _onDelete(this));
    private RelayCommand? _deleteCommand;

    /// <summary>Recomputes the clock-derived fields. Cheap enough for the 30 s tick.</summary>
    public void Refresh(DateTime now)
    {
        _urgency = UrgencyRules.Evaluate(Model.DueAt - now);

        // A finished task has no meaningful "remaining" time; show when it was
        // ticked instead of a countdown that keeps shrinking forever.
        RemainingText = Model.IsDone
            ? Model.CompletedAt is { } completed
                ? $"完成于 {completed:MM-dd HH:mm}"
                : "已完成"
            : UrgencyRules.DescribeRemaining(Model.DueAt, now);

        OnPropertyChanged(nameof(SquareBrush));
        OnPropertyChanged(nameof(IsDone));

        // Title/DueText are derived from the model, which the 编辑 dialog mutates
        // in place, so they must be re-announced here too.
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DueText));
    }
}
