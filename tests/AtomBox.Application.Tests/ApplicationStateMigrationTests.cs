using AtomBox.Application.Accounts;
using AtomBox.Application.Settings;
using AtomBox.Application.State;
using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomUI.City.State;

namespace AtomBox.Application.Tests;

public sealed class ApplicationStateMigrationTests
{
    private static readonly ApplicationSettings DefaultSettings = new(
        3,
        TransferOverwritePolicy.Ask,
        KeepCompletedTransfers: true);

    [Fact]
    public async Task InitializerPublishesReadyAccountAndSettingsSnapshots()
    {
        var account = CreateAccount(CredentialRef.From("credential-initial"));
        var accounts = new InMemoryAccountRepository([account]);
        var settings = new FixedSettingsRepository(DefaultSettings);
        var (state, projector) = CreateState(accounts, settings);
        var initializer = new AtomBoxApplicationStateInitializer(projector);
        var accountNotifications = new List<StateChangedEventArgs<AccountCatalogState>>();
        var settingsNotifications = new List<StateChangedEventArgs<ApplicationSettingsState>>();
        using var accountSubscription = state.Accounts.Observe(accountNotifications.Add);
        using var settingsSubscription = state.Settings.Observe(settingsNotifications.Add);

        await initializer.InitializeAsync();

        Assert.Equal(ApplicationProjectionStatus.Ready, state.Accounts.Value.Status);
        Assert.Equal(account.Id, Assert.Single(state.Accounts.Value.Accounts).Id);
        Assert.Equal(ApplicationProjectionStatus.Ready, state.Settings.Value.Status);
        Assert.Equal(DefaultSettings, state.Settings.Value.Settings);
        Assert.True(accountNotifications[0].IsInitial);
        Assert.True(settingsNotifications[0].IsInitial);
        Assert.Equal([0L, 1L], accountNotifications.Select(item => item.Version));
        Assert.Equal([0L, 1L], settingsNotifications.Select(item => item.Version));
    }

    [Fact]
    public async Task InitializerPublishesFaultedInsteadOfEmptyFallbacks()
    {
        var error = new StorageError(
            StorageErrorCode.InfrastructureUnavailable,
            "repository unavailable",
            StorageErrorCategory.Infrastructure,
            isRetryable: true);
        var accounts = new FailingAccountRepository(error);
        var settings = new FailingSettingsRepository(error);
        var (state, projector) = CreateState(accounts, settings);

        await new AtomBoxApplicationStateInitializer(projector).InitializeAsync();

        Assert.Equal(ApplicationProjectionStatus.Faulted, state.Accounts.Value.Status);
        Assert.Empty(state.Accounts.Value.Accounts);
        Assert.Equal(StorageErrorCode.InfrastructureUnavailable, state.Accounts.Value.Failure?.Code);
        Assert.Equal(ApplicationProjectionStatus.Faulted, state.Settings.Value.Status);
        Assert.Null(state.Settings.Value.Settings);
        Assert.Equal(StorageErrorCode.InfrastructureUnavailable, state.Settings.Value.Failure?.Code);
    }

    [Fact]
    public async Task AccountCredentialTransactionPublishesStateWithoutSecretMaterial()
    {
        var accounts = new InMemoryAccountRepository([]);
        var settings = new FixedSettingsRepository(DefaultSettings);
        var credentials = new RecordingCredentialStore();
        var (state, projector) = CreateState(accounts, settings);
        var service = new AccountAppService(
            accounts,
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory(),
            projector,
            credentials);
        await new AtomBoxApplicationStateInitializer(projector).InitializeAsync();

        var addResult = await service.AddAsync(new AddStorageAccountRequest(
            StorageProviderCategory.ObjectStorage,
            StorageProviderId.From("s3"),
            "Primary",
            "https://storage.example",
            "region-1",
            new CredentialSecretMaterial(new Dictionary<string, string>
            {
                ["accessKey"] = "secret-access",
            })));

        Assert.True(addResult.IsSuccess);
        var added = addResult.GetValueOrThrow();
        Assert.Equal(ApplicationProjectionStatus.Ready, state.Accounts.Value.Status);
        var projected = Assert.Single(state.Accounts.Value.Accounts);
        Assert.Equal(added.Id, projected.Id);
        Assert.Equal("credential-1", projected.CredentialRef.Value);
        Assert.DoesNotContain(
            state.Accounts.Value.GetType().GetProperties(),
            property => property.PropertyType == typeof(CredentialSecretMaterial));

        var updateResult = await service.UpdateAsync(new UpdateStorageAccountRequest(
            added.Id,
            "Primary updated",
            added.Endpoint,
            added.Region,
            new CredentialSecretMaterial(new Dictionary<string, string>
            {
                ["accessKey"] = "secret-updated",
            }),
            added.ProviderConfig));

        Assert.True(updateResult.IsSuccess);
        Assert.Contains(CredentialRef.From("credential-1"), credentials.PendingDeletes);
        Assert.Equal("credential-2", Assert.Single(state.Accounts.Value.Accounts).CredentialRef.Value);

        var deleteResult = await service.DeleteAsync(new DeleteStorageAccountRequest(added.Id));

        Assert.True(deleteResult.IsSuccess);
        Assert.Contains(CredentialRef.From("credential-2"), credentials.PendingDeletes);
        Assert.Empty(state.Accounts.Value.Accounts);
    }

    [Fact]
    public async Task ConcurrentAccountRefreshesCannotPublishAnOlderSnapshotLast()
    {
        var firstAccount = CreateAccount(CredentialRef.From("credential-first"));
        var secondAccount = CreateAccount(CredentialRef.From("credential-second"));
        var accounts = new SequencedAccountRepository(firstAccount, secondAccount);
        var (state, projector) = CreateState(accounts, new FixedSettingsRepository(DefaultSettings));

        var firstRefresh = projector.RefreshAccountsAsync();
        await accounts.FirstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondRefresh = projector.RefreshAccountsAsync();

        Assert.Equal(1, accounts.ReadCount);
        accounts.ReleaseFirstRead();
        await Task.WhenAll(firstRefresh, secondRefresh);

        Assert.Equal(2, accounts.ReadCount);
        Assert.Equal(secondAccount.Id, Assert.Single(state.Accounts.Value.Accounts).Id);
    }

    [Fact]
    public async Task SettingsMutationsPublishTheCommittedProjection()
    {
        var accounts = new InMemoryAccountRepository([]);
        var settings = new FixedSettingsRepository(DefaultSettings);
        var (state, projector) = CreateState(accounts, settings);
        var service = new SettingsAppService(settings, stateProjector: projector);
        await new AtomBoxApplicationStateInitializer(projector).InitializeAsync();
        var updated = new ApplicationSettings(
            7,
            TransferOverwritePolicy.Overwrite,
            KeepCompletedTransfers: false,
            EnableUploadFingerprintIndex: true);

        var updateResult = await service.UpdateAsync(new UpdateApplicationSettingsRequest(updated));

        Assert.True(updateResult.IsSuccess);
        Assert.Equal(updated, state.Settings.Value.Settings);

        var resetResult = await service.ResetAsync(new ResetApplicationSettingsRequest());

        Assert.True(resetResult.IsSuccess);
        Assert.Equal(DefaultSettings, state.Settings.Value.Settings);
    }

    private static (AtomBoxApplicationState State, ApplicationStateProjector Projector) CreateState(
        IStorageAccountRepository accounts,
        IApplicationSettingsRepository settings)
    {
        var registry = new ApplicationStateRegistry();
        var state = new AtomBoxApplicationState(registry, registry, registry);
        return (state, new ApplicationStateProjector(accounts, settings, state));
    }

    private static StorageAccount CreateAccount(CredentialRef credentialRef)
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            StorageProviderId.From("s3"),
            "Account",
            "https://storage.example",
            "region-1",
            credentialRef,
            now,
            now);
    }

    private sealed class InMemoryAccountRepository : IStorageAccountRepository
    {
        private readonly Dictionary<StorageAccountId, StorageAccount> _accounts;

        public InMemoryAccountRepository(IEnumerable<StorageAccount> accounts)
        {
            _accounts = accounts.ToDictionary(account => account.Id);
        }

        public Task<OperationResult<StorageAccount>> GetByIdAsync(
            StorageAccountId accountId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_accounts.TryGetValue(accountId, out var account)
                ? OperationResult<StorageAccount>.Success(account)
                : OperationResult<StorageAccount>.Failure(StorageError.NotFound("Account not found.")));
        }

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(OperationResult<IReadOnlyList<StorageAccount>>.Success(
                _accounts.Values.ToArray()));
        }

        public Task<OperationResult> AddAsync(
            StorageAccount account,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _accounts.Add(account.Id, account);
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> UpdateAsync(
            StorageAccount account,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _accounts[account.Id] = account;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> DeleteAsync(
            StorageAccountId accountId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _accounts.Remove(accountId);
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FailingAccountRepository : IStorageAccountRepository
    {
        private readonly StorageError _error;

        public FailingAccountRepository(StorageError error) => _error = error;

        public Task<OperationResult<StorageAccount>> GetByIdAsync(StorageAccountId accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<StorageAccount>.Failure(_error));

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<StorageAccount>>.Failure(_error));

        public Task<OperationResult> AddAsync(StorageAccount account, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure(_error));

        public Task<OperationResult> UpdateAsync(StorageAccount account, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure(_error));

        public Task<OperationResult> DeleteAsync(StorageAccountId accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure(_error));
    }

    private sealed class SequencedAccountRepository : IStorageAccountRepository
    {
        private readonly StorageAccount _first;
        private readonly StorageAccount _second;
        private readonly TaskCompletionSource _releaseFirst = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;

        public SequencedAccountRepository(StorageAccount first, StorageAccount second)
        {
            _first = first;
            _second = second;
        }

        public TaskCompletionSource FirstReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReadCount => Volatile.Read(ref _readCount);

        public void ReleaseFirstRead() => _releaseFirst.TrySetResult();

        public Task<OperationResult<StorageAccount>> GetByIdAsync(
            StorageAccountId accountId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            var read = Interlocked.Increment(ref _readCount);
            if (read == 1)
            {
                FirstReadStarted.TrySetResult();
                await _releaseFirst.Task.WaitAsync(cancellationToken);
                return OperationResult<IReadOnlyList<StorageAccount>>.Success([_first]);
            }

            return OperationResult<IReadOnlyList<StorageAccount>>.Success([_second]);
        }

        public Task<OperationResult> AddAsync(
            StorageAccount account,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperationResult> UpdateAsync(
            StorageAccount account,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperationResult> DeleteAsync(
            StorageAccountId accountId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedSettingsRepository : IApplicationSettingsRepository
    {
        private ApplicationSettings _settings;

        public FixedSettingsRepository(ApplicationSettings settings) => _settings = settings;

        public Task<OperationResult<ApplicationSettings>> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<ApplicationSettings>.Success(_settings));

        public Task<OperationResult> SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FailingSettingsRepository : IApplicationSettingsRepository
    {
        private readonly StorageError _error;

        public FailingSettingsRepository(StorageError error) => _error = error;

        public Task<OperationResult<ApplicationSettings>> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<ApplicationSettings>.Failure(_error));

        public Task<OperationResult> SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure(_error));
    }

    private sealed class EmptyTransferTaskStore : ITransferTaskStore
    {
        public Task<OperationResult<TransferTask>> GetByIdAsync(TransferTaskId taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<TransferTask>.Failure(StorageError.NotFound("Task not found.")));

        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<TransferTask>>.Success([]));

        public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());
    }

    private sealed class ThrowingProviderFactory : IStorageProviderFactory
    {
        public Task<OperationResult<IStorageProvider>> CreateAsync(
            StorageAccount account,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Provider creation is not expected.");
    }

    private sealed class RecordingCredentialStore : ICredentialStore
    {
        private int _nextId;

        public List<CredentialRef> PendingDeletes { get; } = [];

        public Task<OperationResult<CredentialRef>> SaveAsync(
            CredentialSecretMaterial material,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var credentialRef = CredentialRef.From($"credential-{Interlocked.Increment(ref _nextId)}");
            return Task.FromResult(OperationResult<CredentialRef>.Success(credentialRef));
        }

        public Task<OperationResult<CredentialLease>> AcquireLeaseAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<bool>> ExistsAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<bool>.Success(true));

        public Task<OperationResult> MarkPendingDeleteAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            PendingDeletes.Add(credentialRef);
            return Task.FromResult(OperationResult.Success());
        }
    }
}
