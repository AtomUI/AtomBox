using AtomBox.Application.Accounts;
using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Localization;

namespace AtomBox.Desktop.Dialogs;

public sealed class AccountDialogWorkflow : IAccountDialogWorkflow
{
    private readonly IDialogService _dialogs;
    private readonly IStorageProviderRegistry _providers;
    private readonly AccountAppService _accounts;
    private readonly IAtomBoxLocalizedText _localization;

    public AccountDialogWorkflow(
        IDialogService dialogs,
        IStorageProviderRegistry providers,
        AccountAppService accounts,
        IAtomBoxLocalizedText localization)
    {
        _dialogs = dialogs;
        _providers = providers;
        _accounts = accounts;
        _localization = localization;
    }

    public async Task<OperationResult<StorageAccountSummary>?> AddAccountAsync(
        StorageProviderCategory? preferredCategory = null,
        StorageProviderId? preferredProviderId = null,
        CancellationToken cancellationToken = default)
    {
        var dialogResult = await _dialogs.ShowAccountDialogAsync(new AccountDialogRequest(
            _localization.GetString("Account", "AddRemoteConnection"),
            _providers.GetAll(),
            preferredCategory,
            preferredProviderId,
            TestConnectionAsync)).ConfigureAwait(true);

        if (dialogResult is null)
        {
            return null;
        }

        var addResult = await _accounts.AddAsync(new AddStorageAccountRequest(
            dialogResult.ProviderCategory,
            dialogResult.ProviderId,
            dialogResult.DisplayName,
            dialogResult.Endpoint,
            dialogResult.Region,
            new CredentialSecretMaterial(dialogResult.CredentialValues),
            dialogResult.ProviderConfig), cancellationToken).ConfigureAwait(false);

        if (addResult.IsFailure)
        {
            return OperationResult<StorageAccountSummary>.Failure(addResult.Error!);
        }

        var account = addResult.GetValueOrThrow();
        return OperationResult<StorageAccountSummary>.Success(account);
    }

    public async Task<OperationResult<StorageAccountSummary>?> EditAccountAsync(
        StorageAccountSummary account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        var dialogResult = await _dialogs.ShowAccountDialogAsync(new AccountDialogRequest(
            _localization.GetString("Account", "EditRemoteConnection"),
            _providers.GetAll(),
            account.ProviderCategory,
            account.ProviderId,
            (result, token) => TestConnectionAsync(result, account.CredentialRef, token),
            account)).ConfigureAwait(true);

        if (dialogResult is null)
        {
            return null;
        }

        var credentialMaterial = dialogResult.CredentialValues.Count > 0
            ? new CredentialSecretMaterial(dialogResult.CredentialValues)
            : null;

        var updateResult = await _accounts.UpdateAsync(new UpdateStorageAccountRequest(
            account.Id,
            dialogResult.DisplayName,
            dialogResult.Endpoint,
            dialogResult.Region,
            credentialMaterial,
            dialogResult.ProviderConfig), cancellationToken).ConfigureAwait(false);

        if (updateResult.IsFailure)
        {
            return OperationResult<StorageAccountSummary>.Failure(updateResult.Error!);
        }

        var updatedAccount = updateResult.GetValueOrThrow();
        return OperationResult<StorageAccountSummary>.Success(updatedAccount);
    }

    public async Task<OperationResult> DeleteAccountAsync(
        StorageAccountSummary account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        var deleteResult = await _accounts.DeleteAsync(
            new DeleteStorageAccountRequest(account.Id),
            cancellationToken).ConfigureAwait(false);
        if (deleteResult.IsFailure)
        {
            return deleteResult;
        }

        return OperationResult.Success();
    }

    private async Task<OperationResult<TestConnectionResult>> TestConnectionAsync(
        AccountDialogResult dialogResult,
        CancellationToken cancellationToken)
    {
        return await _accounts.TestConnectionDraftAsync(new TestConnectionDraftRequest(
            dialogResult.ProviderCategory,
            dialogResult.ProviderId,
            dialogResult.DisplayName,
            dialogResult.Endpoint,
            dialogResult.Region,
            new CredentialSecretMaterial(dialogResult.CredentialValues),
            dialogResult.ProviderConfig), cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult<TestConnectionResult>> TestConnectionAsync(
        AccountDialogResult dialogResult,
        CredentialRef currentCredentialRef,
        CancellationToken cancellationToken)
    {
        if (dialogResult.CredentialValues.Count == 0)
        {
            var existingCredentialTestResult = await _accounts.TestConnectionDraftAsync(new TestConnectionDraftRequest(
                dialogResult.ProviderCategory,
                dialogResult.ProviderId,
                dialogResult.DisplayName,
                dialogResult.Endpoint,
                dialogResult.Region,
                currentCredentialRef,
                dialogResult.ProviderConfig), cancellationToken).ConfigureAwait(false);

            return existingCredentialTestResult;
        }

        return await TestConnectionAsync(dialogResult, cancellationToken).ConfigureAwait(false);
    }
}
