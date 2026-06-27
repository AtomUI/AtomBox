using AtomBox.Application.Accounts;
using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Desktop.Dialogs;

public sealed class AccountDialogWorkflow : IAccountDialogWorkflow
{
    private readonly IDialogService _dialogs;
    private readonly IStorageProviderRegistry _providers;
    private readonly ICredentialStore _credentials;
    private readonly AccountAppService _accounts;

    public event EventHandler? AccountsChanged;

    public AccountDialogWorkflow(
        IDialogService dialogs,
        IStorageProviderRegistry providers,
        ICredentialStore credentials,
        AccountAppService accounts)
    {
        _dialogs = dialogs;
        _providers = providers;
        _credentials = credentials;
        _accounts = accounts;
    }

    public async Task<OperationResult<StorageAccountSummary?>> AddAccountAsync(
        StorageProviderCategory? preferredCategory = null,
        StorageProviderId? preferredProviderId = null,
        CancellationToken cancellationToken = default)
    {
        var dialogResult = await _dialogs.ShowAccountDialogAsync(new AccountDialogRequest(
            "添加远程连接",
            _providers.GetAll(),
            preferredCategory,
            preferredProviderId,
            TestConnectionAsync)).ConfigureAwait(true);

        if (dialogResult is null)
        {
            return OperationResult<StorageAccountSummary?>.Success(null);
        }

        var credentialResult = await _credentials.SaveAsync(
            new CredentialSecretMaterial(dialogResult.CredentialValues),
            cancellationToken).ConfigureAwait(false);
        if (credentialResult.IsFailure)
        {
            return OperationResult<StorageAccountSummary?>.Failure(credentialResult.Error!);
        }

        var credentialRef = credentialResult.GetValueOrThrow();
        var addResult = await _accounts.AddAsync(new AddStorageAccountRequest(
            dialogResult.ProviderCategory,
            dialogResult.ProviderId,
            dialogResult.DisplayName,
            dialogResult.Endpoint,
            dialogResult.Region,
            credentialRef,
            dialogResult.ProviderConfig), cancellationToken).ConfigureAwait(false);

        if (addResult.IsFailure)
        {
            await _credentials.MarkPendingDeleteAsync(credentialRef, cancellationToken).ConfigureAwait(false);
            return OperationResult<StorageAccountSummary?>.Failure(addResult.Error!);
        }

        var account = addResult.GetValueOrThrow();
        AccountsChanged?.Invoke(this, EventArgs.Empty);
        return OperationResult<StorageAccountSummary?>.Success(account);
    }

    public async Task<OperationResult<StorageAccountSummary?>> EditAccountAsync(
        StorageAccountSummary account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        var dialogResult = await _dialogs.ShowAccountDialogAsync(new AccountDialogRequest(
            "编辑远程连接",
            _providers.GetAll(),
            account.ProviderCategory,
            account.ProviderId,
            (result, token) => TestConnectionAsync(result, account.CredentialRef, token),
            account)).ConfigureAwait(true);

        if (dialogResult is null)
        {
            return OperationResult<StorageAccountSummary?>.Success(null);
        }

        var credentialRef = account.CredentialRef;
        var hasNewCredential = dialogResult.CredentialValues.Count > 0;
        if (hasNewCredential)
        {
            var credentialResult = await _credentials.SaveAsync(
                new CredentialSecretMaterial(dialogResult.CredentialValues),
                cancellationToken).ConfigureAwait(false);
            if (credentialResult.IsFailure)
            {
                return OperationResult<StorageAccountSummary?>.Failure(credentialResult.Error!);
            }

            credentialRef = credentialResult.GetValueOrThrow();
        }

        var updateResult = await _accounts.UpdateAsync(new UpdateStorageAccountRequest(
            account.Id,
            dialogResult.DisplayName,
            dialogResult.Endpoint,
            dialogResult.Region,
            credentialRef,
            dialogResult.ProviderConfig), cancellationToken).ConfigureAwait(false);

        if (updateResult.IsFailure)
        {
            if (hasNewCredential)
            {
                await _credentials.MarkPendingDeleteAsync(credentialRef, cancellationToken).ConfigureAwait(false);
            }

            return OperationResult<StorageAccountSummary?>.Failure(updateResult.Error!);
        }

        if (hasNewCredential)
        {
            await _credentials.MarkPendingDeleteAsync(account.CredentialRef, cancellationToken).ConfigureAwait(false);
        }

        var updatedAccount = updateResult.GetValueOrThrow();
        AccountsChanged?.Invoke(this, EventArgs.Empty);
        return OperationResult<StorageAccountSummary?>.Success(updatedAccount);
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

        await _credentials.MarkPendingDeleteAsync(account.CredentialRef, cancellationToken).ConfigureAwait(false);
        AccountsChanged?.Invoke(this, EventArgs.Empty);
        return OperationResult.Success();
    }

    private async Task<OperationResult<TestConnectionResult>> TestConnectionAsync(
        AccountDialogResult dialogResult,
        CancellationToken cancellationToken)
    {
        var credentialResult = await _credentials.SaveAsync(
            new CredentialSecretMaterial(dialogResult.CredentialValues),
            cancellationToken).ConfigureAwait(false);
        if (credentialResult.IsFailure)
        {
            return OperationResult<TestConnectionResult>.Failure(credentialResult.Error!);
        }

        var credentialRef = credentialResult.GetValueOrThrow();
        try
        {
            var testResult = await _accounts.TestConnectionDraftAsync(new TestConnectionDraftRequest(
                dialogResult.ProviderCategory,
                dialogResult.ProviderId,
                dialogResult.DisplayName,
                dialogResult.Endpoint,
                dialogResult.Region,
                credentialRef,
                dialogResult.ProviderConfig), cancellationToken).ConfigureAwait(false);

            return testResult;
        }
        finally
        {
            await _credentials.MarkPendingDeleteAsync(credentialRef, cancellationToken).ConfigureAwait(false);
        }
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
