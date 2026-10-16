namespace TodoWidget.Models;

/// <summary>
/// One to-do entry. The page it appears on is never stored: it is always derived
/// from <see cref="DueAt"/> and <see cref="IsDone"/> so the data file can never
/// disagree with the clock.
/// </summary>
public sealed class TodoTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = string.Empty;

    /// <summary>Deadline, truncated to whole hours (minute/second are always 0).</summary>
    public DateTime DueAt { get; set; }

    public bool IsDone { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public TodoTask Clone() => (TodoTask)MemberwiseClone();
}
