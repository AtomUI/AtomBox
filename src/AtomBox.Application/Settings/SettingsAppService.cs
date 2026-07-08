using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Fingerprints;
using AtomBox.Core.Errors;

namespace AtomBox.Application.Settings;

public sealed class SettingsAppService
{
    private static readonly ApplicationSettings DefaultSettings = new(
        DefaultConcurrency: 3,
        DefaultOverwritePolicy: TransferOverwritePolicy.Ask,
        KeepCompletedTransfers: true,
        EnableUploadFingerprintIndex: false);

    private readonly IApplicationSettingsRepository _settings;
    private readonly IFileFingerprintIndexStore? _fingerprints;

    public SettingsAppService(
        IApplicationSettingsRepository settings,
        IFileFingerprintIndexStore? fingerprints = null)
    {
        _settings = settings;
        _fingerprints = fingerprints;
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

    public async Task<OperationResult<UploadFingerprintIndexStatisticsResult>> GetUploadFingerprintIndexStatisticsAsync(
        GetUploadFingerprintIndexStatisticsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_fingerprints is null)
        {
            return OperationResult<UploadFingerprintIndexStatisticsResult>.Failure(new StorageError(
                StorageErrorCode.InfrastructureUnavailable,
                "Upload fingerprint index store is not available.",
                StorageErrorCategory.Infrastructure));
        }

        var statisticsResult = await _fingerprints.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);
        return statisticsResult.IsFailure
            ? OperationResult<UploadFingerprintIndexStatisticsResult>.Failure(statisticsResult.Error!)
            : OperationResult<UploadFingerprintIndexStatisticsResult>.Success(
                new UploadFingerprintIndexStatisticsResult(statisticsResult.GetValueOrThrow()));
    }

    public Task<OperationResult> ClearUploadFingerprintIndexAsync(
        ClearUploadFingerprintIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_fingerprints is null)
        {
            return Task.FromResult(OperationResult.Failure(new StorageError(
                StorageErrorCode.InfrastructureUnavailable,
                "Upload fingerprint index store is not available.",
                StorageErrorCategory.Infrastructure)));
        }

        return _fingerprints.ClearAsync(cancellationToken);
    }
}
