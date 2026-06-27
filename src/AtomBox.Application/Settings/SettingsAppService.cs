using AtomBox.Core.Results;
using AtomBox.Core.Settings;

namespace AtomBox.Application.Settings;

public sealed class SettingsAppService
{
    private static readonly ApplicationSettings DefaultSettings = new(
        DefaultConcurrency: 3,
        DefaultOverwritePolicy: TransferOverwritePolicy.Ask,
        KeepCompletedTransfers: true);

    private readonly IApplicationSettingsRepository _settings;

    public SettingsAppService(IApplicationSettingsRepository settings)
    {
        _settings = settings;
    }

    public async Task<OperationResult<ApplicationSettingsResult>> GetAsync(
        GetApplicationSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var settingsResult = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        return settingsResult.IsFailure
            ? OperationResult<ApplicationSettingsResult>.Failure(settingsResult.Error!)
            : OperationResult<ApplicationSettingsResult>.Success(new ApplicationSettingsResult(settingsResult.GetValueOrThrow()));
    }

    public async Task<OperationResult<ApplicationSettingsResult>> UpdateAsync(
        UpdateApplicationSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var saveResult = await _settings.SaveAsync(request.Settings, cancellationToken).ConfigureAwait(false);
        return saveResult.IsFailure
            ? OperationResult<ApplicationSettingsResult>.Failure(saveResult.Error!)
            : OperationResult<ApplicationSettingsResult>.Success(new ApplicationSettingsResult(request.Settings));
    }

    public async Task<OperationResult<ApplicationSettingsResult>> ResetAsync(
        ResetApplicationSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var saveResult = await _settings.SaveAsync(DefaultSettings, cancellationToken).ConfigureAwait(false);
        return saveResult.IsFailure
            ? OperationResult<ApplicationSettingsResult>.Failure(saveResult.Error!)
            : OperationResult<ApplicationSettingsResult>.Success(new ApplicationSettingsResult(DefaultSettings));
    }
}
