using AtomBox.Core.Results;

namespace AtomBox.Core.Settings;

public interface IApplicationSettingsRepository
{
    Task<OperationResult<ApplicationSettings>> GetAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult> SaveAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken = default);
}
