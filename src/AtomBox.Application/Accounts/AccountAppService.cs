using AtomBox.Core.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.Credentials;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Application.State;

namespace AtomBox.Application.Accounts;

public sealed class AccountAppService
{
    private readonly IStorageAccountRepository _accounts;
    private readonly ITransferTaskStore _transferTasks;
    private readonly IStorageProviderFactory _providerFactory;
    private readonly ApplicationStateProjector? _stateProjector;
    private readonly ICredentialStore? _credentials;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public AccountAppService(
        IStorageAccountRepository accounts,
        ITransferTaskStore transferTasks,
        IStorageProviderFactory providerFactory,
        ApplicationStateProjector? stateProjector = null,
        ICredentialStore? credentials = null)
    {
        _accounts = accounts;
        _transferTasks = transferTasks;
        _providerFactory = providerFactory;
        _stateProjector = stateProjector;
        _credentials = credentials;
    }

    public async Task<OperationResult<StorageAccountSummary>> AddAsync(
        AddStorageAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await AddCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<OperationResult<StorageAccountSummary>> AddCoreAsync(
        AddStorageAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ProviderId.IsEmpty)
        {
            return OperationResult<StorageAccountSummary>.Failure(StorageError.Validation("Storage provider id is required."));
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return OperationResult<StorageAccountSummary>.Failure(StorageError.Validation("Storage account display name is required."));
        }

        var credentialResult = await ResolveNewCredentialAsync(
            request.CredentialRef,
            request.CredentialMaterial,
            cancellationToken).ConfigureAwait(false);
        if (credentialResult.IsFailure)
        {
            return OperationResult<StorageAccountSummary>.Failure(credentialResult.Error!);
        }

        var credential = credentialResult.GetValueOrThrow();
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            request.ProviderCategory,
            request.ProviderId,
            request.DisplayName,
            request.Endpoint,
            request.Region,
            credential.Reference,
            now,
            now,
            request.ProviderConfig);

        var addResult = await _accounts.AddAsync(account, cancellationToken).ConfigureAwait(false);
        if (addResult.IsFailure)
        {
            await CleanupCredentialAsync(credential).ConfigureAwait(false);
            return OperationResult<StorageAccountSummary>.Failure(addResult.Error!);
        }

        await RefreshAccountsProjectionAsync().ConfigureAwait(false);
        return OperationResult<StorageAccountSummary>.Success(StorageAccountSummary.From(account));
    }

    public async Task<OperationResult<StorageAccountSummary>> UpdateAsync(
        UpdateStorageAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await UpdateCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<OperationResult<StorageAccountSummary>> UpdateCoreAsync(
        UpdateStorageAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Id.IsEmpty)
        {
            return OperationResult<StorageAccountSummary>.Failure(StorageError.Validation("Storage account id is required."));
        }

        if (!request.RetainExistingCredential &&
            request.CredentialMaterial is null &&
            request.CredentialRef.IsEmpty)
        {
            return OperationResult<StorageAccountSummary>.Failure(
                StorageError.Validation("Credential reference is required."));
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return OperationResult<StorageAccountSummary>.Failure(StorageError.Validation("Storage account display name is required."));
        }

        var currentResult = await _accounts.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (currentResult.IsFailure)
        {
            return OperationResult<StorageAccountSummary>.Failure(currentResult.Error!);
        }

        var current = currentResult.GetValueOrThrow();
        var credentialResult = await ResolveUpdatedCredentialAsync(
            request,
            current.CredentialRef,
            cancellationToken).ConfigureAwait(false);
        if (credentialResult.IsFailure)
        {
            return OperationResult<StorageAccountSummary>.Failure(credentialResult.Error!);
        }

        var credential = credentialResult.GetValueOrThrow();
        var updated = current.UpdateConfiguration(
            request.DisplayName,
            request.Endpoint,
            request.Region,
            credential.Reference,
            DateTimeOffset.UtcNow,
            request.ProviderConfig);

        var updateResult = await _accounts.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        if (updateResult.IsFailure)
        {
            await CleanupCredentialAsync(credential).ConfigureAwait(false);
            return OperationResult<StorageAccountSummary>.Failure(updateResult.Error!);
        }

        if (credential.IsNew)
        {
            await MarkCredentialPendingDeleteAsync(current.CredentialRef).ConfigureAwait(false);
        }

        await RefreshAccountsProjectionAsync().ConfigureAwait(false);
        return OperationResult<StorageAccountSummary>.Success(StorageAccountSummary.From(updated));
    }

    public async Task<OperationResult> DeleteAsync(
        DeleteStorageAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await DeleteCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<OperationResult> DeleteCoreAsync(
        DeleteStorageAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Id.IsEmpty)
        {
            return OperationResult.Failure(StorageError.Validation("Storage account id is required."));
        }

        var tasksResult = await _transferTasks.ListAsync(cancellationToken).ConfigureAwait(false);
        if (tasksResult.IsFailure)
        {
            return OperationResult.Failure(tasksResult.Error!);
        }

        var hasUnfinishedTask = tasksResult.GetValueOrThrow().Any(task =>
            task.StorageAccountId == request.Id &&
            task.Status is TransferStatus.Pending or TransferStatus.Running or TransferStatus.Paused or TransferStatus.Interrupted);

        if (hasUnfinishedTask)
        {
            return OperationResult.Failure(new StorageError(
                StorageErrorCode.Conflict,
                "Storage account is still referenced by unfinished transfer tasks.",
                StorageErrorCategory.Conflict));
        }

        var credentialRef = default(CredentialRef);
        if (_credentials is not null)
        {
            var currentResult = await _accounts.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);
            if (currentResult.IsFailure)
            {
                return OperationResult.Failure(currentResult.Error!);
            }

            credentialRef = currentResult.GetValueOrThrow().CredentialRef;
        }

        var deleteResult = await _accounts.DeleteAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (deleteResult.IsFailure)
        {
            return deleteResult;
        }

        await MarkCredentialPendingDeleteAsync(credentialRef).ConfigureAwait(false);

        await RefreshAccountsProjectionAsync().ConfigureAwait(false);
        return OperationResult.Success();
    }

    public async Task<OperationResult<IReadOnlyList<StorageAccountSummary>>> ListAsync(
        ListStorageAccountsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_stateProjector is not null)
        {
            var projectionResult = await _stateProjector
                .RefreshAccountsAsync(cancellationToken)
                .ConfigureAwait(false);
            if (projectionResult.IsFailure)
            {
                return projectionResult;
            }

            var projectedAccounts = projectionResult.GetValueOrThrow()
                .Where(account => request.ProviderCategory is null || account.ProviderCategory == request.ProviderCategory)
                .ToArray();
            return OperationResult<IReadOnlyList<StorageAccountSummary>>.Success(projectedAccounts);
        }

        var accountsResult = await _accounts.ListAsync(cancellationToken).ConfigureAwait(false);
        if (accountsResult.IsFailure)
        {
            return OperationResult<IReadOnlyList<StorageAccountSummary>>.Failure(accountsResult.Error!);
        }

        var summaries = accountsResult.GetValueOrThrow()
            .Where(account => request.ProviderCategory is null || account.ProviderCategory == request.ProviderCategory)
            .Select(StorageAccountSummary.From)
            .ToArray();

        return OperationResult<IReadOnlyList<StorageAccountSummary>>.Success(summaries);
    }

    public async Task<OperationResult<TestConnectionResult>> TestConnectionAsync(
        TestConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Id.IsEmpty)
        {
            return OperationResult<TestConnectionResult>.Failure(StorageError.Validation("Storage account id is required."));
        }

        var accountResult = await _accounts.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (accountResult.IsFailure)
        {
            return OperationResult<TestConnectionResult>.Failure(accountResult.Error!);
        }

        return await TestConnectionAsync(accountResult.GetValueOrThrow(), cancellationToken).ConfigureAwait(false);
    }

    public Task<OperationResult<TestConnectionResult>> TestConnectionDraftAsync(
        TestConnectionDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProviderId.IsEmpty)
        {
            return Task.FromResult(OperationResult<TestConnectionResult>.Failure(
                StorageError.Validation("Storage provider id is required.")));
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Task.FromResult(OperationResult<TestConnectionResult>.Failure(
                StorageError.Validation("Storage account display name is required.")));
        }

        return TestConnectionDraftCoreAsync(request, cancellationToken);
    }

    private async Task<OperationResult<TestConnectionResult>> TestConnectionAsync(
        StorageAccount account,
        CancellationToken cancellationToken)
    {
        var providerResult = await _providerFactory.CreateAsync(account, cancellationToken).ConfigureAwait(false);
        if (providerResult.IsFailure)
        {
            return OperationResult<TestConnectionResult>.Failure(providerResult.Error!);
        }

        await using var provider = providerResult.GetValueOrThrow();
        var probePath = GetConnectionProbePath(account);
        var probeResult = await provider.ListAsync(probePath, cancellationToken).ConfigureAwait(false);
        if (probeResult.IsFailure)
        {
            return OperationResult<TestConnectionResult>.Failure(probeResult.Error!);
        }

        return OperationResult<TestConnectionResult>.Success(new TestConnectionResult(
            true,
            provider.Capabilities,
            account.ProviderId,
            probePath.ToString(),
            account.Endpoint,
            account.Region,
            account.GetProviderConfigValue("bucket"),
            provider is IRemoteHomePathProvider homePathProvider ? homePathProvider.HomePath : null));
    }

    private static RemotePath GetConnectionProbePath(StorageAccount account)
    {
        return account.ProviderCategory == StorageProviderCategory.ObjectStorage &&
            account.GetProviderConfigValue("bucket") is { Length: > 0 } bucket
            ? new RemotePath(bucket, RemotePathKind.BucketRoot)
            : RemotePath.Root;
    }

    private async Task<OperationResult<TestConnectionResult>> TestConnectionDraftCoreAsync(
        TestConnectionDraftRequest request,
        CancellationToken cancellationToken)
    {
        var credentialResult = await ResolveNewCredentialAsync(
            request.CredentialRef,
            request.CredentialMaterial,
            cancellationToken).ConfigureAwait(false);
        if (credentialResult.IsFailure)
        {
            return OperationResult<TestConnectionResult>.Failure(credentialResult.Error!);
        }

        var credential = credentialResult.GetValueOrThrow();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var account = new StorageAccount(
                StorageAccountId.New(),
                request.ProviderCategory,
                request.ProviderId,
                request.DisplayName,
                request.Endpoint,
                request.Region,
                credential.Reference,
                now,
                now,
                request.ProviderConfig);

            return await TestConnectionAsync(account, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await CleanupCredentialAsync(credential).ConfigureAwait(false);
        }
    }

    private async Task RefreshAccountsProjectionAsync()
    {
        if (_stateProjector is not null)
        {
            await _stateProjector.RefreshAccountsAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<OperationResult<ResolvedCredential>> ResolveNewCredentialAsync(
        CredentialRef credentialRef,
        CredentialSecretMaterial? material,
        CancellationToken cancellationToken)
    {
        if (material is null)
        {
            return credentialRef.IsEmpty
                ? OperationResult<ResolvedCredential>.Failure(StorageError.Validation("Credential reference is required."))
                : OperationResult<ResolvedCredential>.Success(new ResolvedCredential(credentialRef, IsNew: false));
        }

        if (_credentials is null)
        {
            return OperationResult<ResolvedCredential>.Failure(new StorageError(
                StorageErrorCode.InfrastructureUnavailable,
                "Credential store is not available.",
                StorageErrorCategory.Infrastructure));
        }

        var saveResult = await _credentials.SaveAsync(material, cancellationToken).ConfigureAwait(false);
        return saveResult.IsFailure
            ? OperationResult<ResolvedCredential>.Failure(saveResult.Error!)
            : OperationResult<ResolvedCredential>.Success(
                new ResolvedCredential(saveResult.GetValueOrThrow(), IsNew: true));
    }

    private Task<OperationResult<ResolvedCredential>> ResolveUpdatedCredentialAsync(
        UpdateStorageAccountRequest request,
        CredentialRef currentCredentialRef,
        CancellationToken cancellationToken)
    {
        if (request.RetainExistingCredential)
        {
            return Task.FromResult(OperationResult<ResolvedCredential>.Success(
                new ResolvedCredential(currentCredentialRef, IsNew: false)));
        }

        return ResolveNewCredentialAsync(
            request.CredentialRef,
            request.CredentialMaterial,
            cancellationToken);
    }

    private Task CleanupCredentialAsync(ResolvedCredential credential)
    {
        return credential.IsNew
            ? MarkCredentialPendingDeleteAsync(credential.Reference)
            : Task.CompletedTask;
    }

    private async Task MarkCredentialPendingDeleteAsync(CredentialRef credentialRef)
    {
        if (_credentials is not null && !credentialRef.IsEmpty)
        {
            await _credentials.MarkPendingDeleteAsync(
                credentialRef,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed record ResolvedCredential(CredentialRef Reference, bool IsNew);
}
