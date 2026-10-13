using AtomBox.Application.Accounts;
using AtomBox.Core.Accounts;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;

namespace AtomBox.Application.State;

public sealed class ApplicationStateProjector
{
    private readonly IStorageAccountRepository _accounts;
    private readonly IApplicationSettingsRepository _settings;
    private readonly AtomBoxApplicationState _state;
    private readonly SemaphoreSlim _accountsProjectionGate = new(1, 1);
    private readonly SemaphoreSlim _settingsProjectionGate = new(1, 1);

    public ApplicationStateProjector(
        IStorageAccountRepository accounts,
        IApplicationSettingsRepository settings,
        AtomBoxApplicationState state)
    {
        _accounts = accounts;
        _settings = settings;
        _state = state;
    }

    public async Task<OperationResult<IReadOnlyList<StorageAccountSummary>>> RefreshAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        await _accountsProjectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _accounts.ListAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                _state.PublishAccounts(AccountCatalogState.Faulted(result.Error!));
                return OperationResult<IReadOnlyList<StorageAccountSummary>>.Failure(result.Error!);
            }

            var accounts = result.GetValueOrThrow()
                .Select(StorageAccountSummary.From)
                .ToArray();
            _state.PublishAccounts(AccountCatalogState.Ready(accounts));
            return OperationResult<IReadOnlyList<StorageAccountSummary>>.Success(accounts);
        }
        finally
        {
            _accountsProjectionGate.Release();
        }
    }

    public async Task<OperationResult<ApplicationSettings>> RefreshSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        await _settingsProjectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                _state.PublishSettings(ApplicationSettingsState.Faulted(result.Error!));
                return OperationResult<ApplicationSettings>.Failure(result.Error!);
            }

            var settings = result.GetValueOrThrow();
            _state.PublishSettings(ApplicationSettingsState.Ready(settings));
            return OperationResult<ApplicationSettings>.Success(settings);
        }
        finally
        {
            _settingsProjectionGate.Release();
        }
    }
}
