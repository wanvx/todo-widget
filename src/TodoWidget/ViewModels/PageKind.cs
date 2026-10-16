using TodoWidget.Models;

namespace TodoWidget.ViewModels;

/// <summary>Which of the three pages is on screen.</summary>
public enum PageKind
{
    Active,
    Done,
    Overdue,
}

/// <summary>
/// Implemented by the view. Keeps window-opening out of the view-model while
/// still letting the view-model drive add / edit / confirm flows.
/// </summary>
public interface ITaskEditor
{
    /// <summary>Opens the 添加 dialog; returns the new task or null when cancelled.</summary>
    TodoTask? CreateTask(DateTime defaultDue);

    /// <summary>Opens the 编辑 dialog; returns true when the task was changed.</summary>
    bool EditTask(TodoTask task);

    bool Confirm(string message);
}
