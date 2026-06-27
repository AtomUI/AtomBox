using AtomBox.Desktop.ViewModels;

namespace AtomBox.Desktop.Navigation;

public sealed class NavigationItemViewModel : ViewModelBase
{
    public NavigationItemViewModel(string title, NavigationTarget? target = null, object? parameter = null)
    {
        Title = title;
        Target = target;
        Parameter = parameter;
    }

    public string Title { get; }

    public NavigationTarget? Target { get; }

    public object? Parameter { get; }
}
