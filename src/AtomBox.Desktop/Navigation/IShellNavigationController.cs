using AtomUI.City.Mvvm;

namespace AtomBox.Desktop.Navigation;

/// <summary>
/// Owns navigation commands that can replace the currently active content page.
/// </summary>
public interface IShellNavigationController : IActivationScopeOwner
{
    Task NavigateByMenuKeyAsync(
        string? itemKey,
        CancellationToken cancellationToken = default);
}
