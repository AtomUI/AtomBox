using AtomBox.Application.Transfers;
using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.DependencyInjection;
using AtomBox.Providers.Common;
using AtomBox.Transfer.DependencyInjection;
using AtomBox.Transfer.Scheduling;
using AtomBox.Transfer.State;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.EndToEnd.Tests;

public sealed class TransferRuntimeEndToEndTests
{
    [Fact]
    public async Task ApplicationSubmissionFlowsThroughRuntimeProviderJsonStoresAndCityState()
    {
        var root = Path.Combine(Path.GetTempPath(), "AtomBox.Transfer.E2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "source.bin");
        await File.WriteAllBytesAsync(sourcePath, Enumerable.Range(0, 128 * 1024)
            .Select(index => (byte)(index % 251))
            .ToArray());

        var account = CreateAccount();
        var paths = new AtomBoxStoragePaths(root);
        var services = new ServiceCollection();
        services.AddAtomBoxInfrastructure();
        services.AddSingleton(paths);
        services.AddSingleton<IStorageAccountRepository>(new FixedAccountRepository(account));
        services.AddSingleton<IStorageProviderFactory>(new FakeProviderFactory());
        services.AddAtomBoxTransfer();

        try
        {
            await using var provider = services.BuildServiceProvider();
            var runtime = provider.GetRequiredService<TransferRuntimeController>();
            await runtime.StartAsync(CancellationToken.None);
            var application = new TransferAppService(
                provider.GetRequiredService<ITransferTaskScheduler>(),
                provider.GetRequiredService<ITransferStateStore>(),
                provider.GetRequiredService<ITransferTaskStore>());

            var submission = await application.CreateUploadTasksAsync(new CreateUploadTasksRequest(
                account.Id,
                [new LocalPath(sourcePath)],
                new RemotePath("bucket/source.bin"),
                TransferOverwritePolicy.Ask));

            Assert.True(submission.IsSuccess);
            var submitted = Assert.Single(submission.GetValueOrThrow().Tasks);
            Assert.Equal(TransferStatus.Pending, submitted.Status);

            TransferStateSnapshot? completed = null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (completed is null)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var history = await application.GetHistoryAsync(
                    new GetTransferHistoryRequest(),
                    timeout.Token);
                Assert.True(history.IsSuccess);
                completed = history.GetValueOrThrow().Tasks
                    .SingleOrDefault(snapshot => snapshot.Task.Id == submitted.Id);
                if (completed is null)
                {
                    await Task.Delay(10, timeout.Token);
                }
            }

            Assert.Equal(TransferStatus.Succeeded, completed.Task.Status);
            Assert.Equal(100, completed.Progress?.Percent);
            Assert.Equal(TransferRuntimeLifecycleState.Running, runtime.Current.Lifecycle);
            Assert.Equal(TransferRuntimeHealthState.Healthy, runtime.Current.Health);
            Assert.True(File.Exists(paths.TransferTasksFile));
            Assert.True(File.Exists(paths.TransferProgressFile));

            await runtime.StopAsync();
            Assert.Equal(TransferRuntimeLifecycleState.Stopped, runtime.Current.Lifecycle);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    private static StorageAccount CreateAccount()
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            StorageProviderId.From("e2e-fake"),
            "E2E fake account",
            null,
            null,
            CredentialRef.From("e2e-fake-credential"),
            now,
            now);
    }

    private static async Task DeleteDirectoryEventuallyAsync(string path)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(25);
            }
        }
    }

    private sealed class FixedAccountRepository(StorageAccount account) : IStorageAccountRepository
    {
        public Task<OperationResult<StorageAccount>> GetByIdAsync(
            StorageAccountId accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(accountId == account.Id
                ? OperationResult<StorageAccount>.Success(account)
                : OperationResult<StorageAccount>.Failure(
                    AtomBox.Core.Errors.StorageError.NotFound("Account not found.")));

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OperationResult<IReadOnlyList<StorageAccount>>>(
                OperationResult<IReadOnlyList<StorageAccount>>.Success([account]));

        public Task<OperationResult> AddAsync(
            StorageAccount value,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public Task<OperationResult> UpdateAsync(
            StorageAccount value,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(
            StorageAccountId accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());
    }

    private sealed class FakeProviderFactory : IStorageProviderFactory
    {
        private static readonly ProviderDescriptor Descriptor = new(
            StorageProviderId.From("e2e-fake"),
            StorageProviderCategory.ObjectStorage,
            "E2E fake",
            "In-memory provider used by the runtime end-to-end gate.",
            StorageCapabilitySet.Empty,
            []);

        public Task<OperationResult<IStorageProvider>> CreateAsync(
            StorageAccount account,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OperationResult<IStorageProvider>>(
                OperationResult<IStorageProvider>.Success(new FakeStorageProvider(Descriptor)));
    }
}
