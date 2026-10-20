namespace AtomBox.Application.State;

public sealed class AtomBoxApplicationStateInitializer
{
    private readonly ApplicationStateProjector _projector;

    public AtomBoxApplicationStateInitializer(ApplicationStateProjector projector)
    {
        _projector = projector;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _projector.RefreshAccountsAsync(cancellationToken).ConfigureAwait(false);
        await _projector.RefreshSettingsAsync(cancellationToken).ConfigureAwait(false);
    }
}
