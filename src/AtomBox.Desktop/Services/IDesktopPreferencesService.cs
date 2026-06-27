namespace AtomBox.Desktop.Services;

public interface IDesktopPreferencesService
{
    Task<DesktopPreferences> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(DesktopPreferences preferences, CancellationToken cancellationToken = default);

    Task<DesktopPreferences> ResetAsync(CancellationToken cancellationToken = default);
}
