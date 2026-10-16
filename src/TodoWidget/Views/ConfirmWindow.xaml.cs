using System.Windows;
using System.Windows.Input;

namespace TodoWidget.Views;

/// <summary>
/// Themed stand-in for MessageBox so destructive confirmations match the
/// minimal black-and-white look instead of the native Windows dialog.
/// </summary>
public partial class ConfirmWindow : Window
{
    private ConfirmWindow(string message)
    {
        InitializeComponent();
        MessageText.Text = message;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) DialogResult = false;
            else if (e.Key == Key.Enter) DialogResult = true;
        };

        Loaded += (_, _) => Activate();
    }

    public static bool Ask(string message)
    {
        var dialog = new ConfirmWindow(message);
        return dialog.ShowDialog() == true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
