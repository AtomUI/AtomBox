using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Fingerprints;
using AtomBox.Core.Errors;
using AtomBox.Application.State;

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
    private readonly ApplicationStateProjector? _stateProjector;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public SettingsAppService(
        IApplicationSettingsRepository settings,
        IFileFingerprintIndexStore? fingerprints = null,
        ApplicationStateProjector? stateProjector = null)
    {
        _settings = settings;
        _fingerprints = fingerprints;
        _stateProjector = stateProjector;
    }

    public async Task<OperationResult<ApplicationSettingsResult>> GetAsync(
        GetApplicationSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var settingsResult = _stateProjector is null
            ? await _settings.GetAsync(cancellationToken).ConfigureAwait(false)
            : await _stateProjector.RefreshSettingsAsync(cancellationToken).ConfigureAwait(false);
        return settingsResult.IsFailure
            ? OperationResult<ApplicationSettingsResult>.Failure(settingsResult.Error!)
            : OperationResult<ApplicationSettingsResult>.Success(new ApplicationSettingsResult(settingsResult.GetValueOrThrow()));
    }

    public async Task<OperationResult<ApplicationSettingsResult>> UpdateAsync(
        UpdateApplicationSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var saveResult = await _settings.SaveAsync(request.Settings, cancellationToken).ConfigureAwait(false);
            if (saveResult.IsFailure)
            {
                return OperationResult<ApplicationSettingsResult>.Failure(saveResult.Error!);
            }

            await RefreshSettingsProjectionAsync().ConfigureAwait(false);
            return OperationResult<ApplicationSettingsResult>.Success(new ApplicationSettingsResult(request.Settings));
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<OperationResult<ApplicationSettingsResult>> ResetAsync(
        ResetApplicationSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var saveResult = await _settings.SaveAsync(DefaultSettings, cancellationToken).ConfigureAwait(false);
            if (saveResult.IsFailure)
            {
                return OperationResult<ApplicationSettingsResult>.Failure(saveResult.Error!);
            }

            await RefreshSettingsProjectionAsync().ConfigureAwait(false);
            return OperationResult<ApplicationSettingsResult>.Success(new ApplicationSettingsResult(DefaultSettings));
        }
        finally
        {
            _mutationGate.Release();
        }
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

    private async Task RefreshSettingsProjectionAsync()
    {
        if (_stateProjector is not null)
        {
            await _stateProjector.RefreshSettingsAsync(CancellationToken.None).ConfigureAwait(false);
        }
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
