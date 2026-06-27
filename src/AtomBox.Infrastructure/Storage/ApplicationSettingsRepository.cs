using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Infrastructure.Configuration;

namespace AtomBox.Infrastructure.Storage;

public sealed class ApplicationSettingsRepository : IApplicationSettingsRepository
{
    private static readonly ApplicationSettings DefaultSettings = new(
        DefaultConcurrency: 3,
        DefaultOverwritePolicy: TransferOverwritePolicy.Ask,
        KeepCompletedTransfers: true);

    private readonly JsonFileStore<ApplicationSettings> _store;

    public ApplicationSettingsRepository(AtomBoxStoragePaths paths)
    {
        _store = new JsonFileStore<ApplicationSettings>(paths.SettingsFile);
    }

    public Task<OperationResult<ApplicationSettings>> GetAsync(CancellationToken cancellationToken = default)
    {
        return _store.ReadAsync(DefaultSettings, cancellationToken);
    }

    public Task<OperationResult> SaveAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken = default)
    {
        return _store.WriteAsync(settings, cancellationToken);
    }
}
