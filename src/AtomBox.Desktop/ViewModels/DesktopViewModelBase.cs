using System.ComponentModel;
using System.Runtime.CompilerServices;
using AtomUI.City.Mvvm;

namespace AtomBox.Desktop.ViewModels;

/// <summary>
/// Provides AtomBox Desktop conveniences on top of the City ViewModel lifecycle.
/// </summary>
public abstract class DesktopViewModelBase : ViewModelBase
{
    /// <summary>
    /// Raises a property-changed notification by property name.
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
    }
}
