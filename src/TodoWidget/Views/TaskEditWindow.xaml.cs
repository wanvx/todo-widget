using System.Windows;
using System.Windows.Input;
using TodoWidget.Models;

namespace TodoWidget.Views;

/// <summary>添加 / 编辑 task dialog. Whole-hour precision by design.</summary>
public partial class TaskEditWindow : Window
{
    private readonly TodoTask _draft;

    public TaskEditWindow(TodoTask draft, bool isNew)
    {
        _draft = draft;

        InitializeComponent();

        HeadingText.Text = isNew ? "添加任务" : "编辑任务";
        TitleBox.Text = draft.Title;

        HourBox.ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToList();
        HourBox.SelectedIndex = draft.DueAt.Hour;
        DateBox.SelectedDate = draft.DueAt.Date;

        Loaded += (_, _) =>
        {
            Activate();
            TitleBox.Focus();
            TitleBox.SelectAll();
        };

        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        // Enter saves, except while the calendar popup is open.
        if (e.Key == Key.Enter && !DateBox.IsDropDownOpen)
        {
            TrySave();
            e.Handled = true;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Save_Click(object sender, RoutedEventArgs e) => TrySave();

    private void TrySave()
    {
        var title = TitleBox.Text.Trim();

        if (title.Length == 0)
        {
            ShowError("请输入任务名称。");
            TitleBox.Focus();
            return;
        }

        var date = DateBox.SelectedDate ?? DateTime.Today;
        var hour = HourBox.SelectedIndex < 0 ? 0 : HourBox.SelectedIndex;

        _draft.Title = title;
        _draft.DueAt = new DateTime(date.Year, date.Month, date.Day, hour, 0, 0);

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
