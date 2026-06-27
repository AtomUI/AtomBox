namespace AtomBox.Desktop.Navigation;

public interface INavigationService
{
    event EventHandler? CurrentViewModelChanged;

    event EventHandler? CurrentMenuKeyChanged;

    object? CurrentViewModel { get; }

    string? CurrentMenuKey { get; }

    Task NavigateAsync(NavigationTarget target, object? parameter = null);
}
