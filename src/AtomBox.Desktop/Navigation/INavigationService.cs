namespace AtomBox.Desktop.Navigation;

public interface INavigationService
{
    event EventHandler? CurrentMenuKeyChanged;

    string? CurrentMenuKey { get; }

    Task NavigateAsync(
        NavigationTarget target,
        object? parameter = null,
        CancellationToken cancellationToken = default);
}
