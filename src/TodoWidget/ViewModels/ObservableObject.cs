using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TodoWidget.ViewModels;

/// <summary>
/// Minimal INotifyPropertyChanged base. Hand-rolled on purpose: pulling in
/// CommunityToolkit.Mvvm would add a NuGet dependency to an app that must build
/// and publish completely offline.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
