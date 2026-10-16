namespace TodoWidget.Models;

/// <summary>Colour bucket of the square shown in front of every task.</summary>
public enum Urgency
{
    Green,
    Yellow,
    Orange,
    Red,
}

/// <summary>Which page a task is rendered on.</summary>
public enum TaskBucket
{
    /// <summary>未完成，且尚未超时满 24 小时。</summary>
    Active,

    /// <summary>已打勾。</summary>
    Done,

    /// <summary>未完成，且已超过截止时间 24 小时。</summary>
    Overdue,
}

/// <summary>
/// Thresholds exactly as specified:
/// 绿 = 剩余 &gt; 7 天 / 黄 = 3 天 &lt; 剩余 ≤ 7 天 /
/// 橙 = 36 小时 &lt; 剩余 ≤ 3 天 / 红 = 剩余 ≤ 36 小时（含已超时）。
/// </summary>
public static class UrgencyRules
{
    public static readonly TimeSpan GreenThreshold = TimeSpan.FromDays(7);
    public static readonly TimeSpan YellowThreshold = TimeSpan.FromDays(3);
    public static readonly TimeSpan OrangeThreshold = TimeSpan.FromHours(36);

    /// <summary>How long an overdue task stays on the 待完成 page before moving to 超时.</summary>
    public static readonly TimeSpan OverdueGrace = TimeSpan.FromHours(24);

    public static Urgency Evaluate(TimeSpan remaining)
    {
        if (remaining > GreenThreshold) return Urgency.Green;
        if (remaining > YellowThreshold) return Urgency.Yellow;
        if (remaining > OrangeThreshold) return Urgency.Orange;
        return Urgency.Red;
    }

    public static TaskBucket BucketOf(TodoTask task, DateTime now)
    {
        if (task.IsDone) return TaskBucket.Done;
        return now >= task.DueAt + OverdueGrace ? TaskBucket.Overdue : TaskBucket.Active;
    }

    /// <summary>Rounds a user supplied deadline down/up to the supported whole-hour precision.</summary>
    public static DateTime Normalize(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, 0, 0, value.Kind);

    /// <summary>Human readable remaining time, e.g. "剩余 3天2小时" or "已超时 5小时".</summary>
    public static string DescribeRemaining(DateTime dueAt, DateTime now)
    {
        var delta = dueAt - now;
        var overdue = delta < TimeSpan.Zero;
        var abs = overdue ? -delta : delta;

        var totalHours = (long)Math.Floor(abs.TotalHours);
        var days = totalHours / 24;
        var hours = totalHours % 24;

        string amount = days > 0 ? $"{days}天{hours}小时" : $"{hours}小时";
        // A deadline that just passed would otherwise read "已超时 0小时".
        if (overdue && totalHours == 0) amount = "不足1小时";

        return overdue ? $"已超时 {amount}" : $"剩余 {amount}";
    }
}
