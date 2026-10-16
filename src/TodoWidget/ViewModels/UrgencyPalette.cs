using System.Windows.Media;
using TodoWidget.Models;

namespace TodoWidget.ViewModels;

/// <summary>
/// The only four non-grey colours in the whole app. Brushes are frozen once
/// (thread-safe + cheaper for WPF to render) as recommended for Freezables.
/// </summary>
public static class UrgencyPalette
{
    public static readonly Brush Green = Frozen("#22A06B");
    public static readonly Brush Yellow = Frozen("#D9A400");
    public static readonly Brush Orange = Frozen("#EF7B24");
    public static readonly Brush Red = Frozen("#E03131");
    public static readonly Brush Muted = Frozen("#B9BDC4");

    public static Brush For(Urgency urgency) => urgency switch
    {
        Urgency.Green => Green,
        Urgency.Yellow => Yellow,
        Urgency.Orange => Orange,
        _ => Red,
    };

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
