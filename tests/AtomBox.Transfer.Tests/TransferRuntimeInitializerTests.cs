using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Transfer.Scheduling;

namespace AtomBox.Transfer.Tests;

public sealed class TransferRuntimeInitializerTests
{
    [Fact]
    public async Task InitializeAsync_NoRunningTasks_Success()
    {
        var store = new MemoryTransferStore();
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Pending));
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Succeeded));
        var initializer = new TransferRuntimeInitializer(store);

        var result = await initializer.InitializeAsync();

        Assert.True(result.IsSuccess);
        Assert.All(store.Tasks, t => Assert.NotEqual(TransferStatus.Interrupted, t.Status));
    }

    [Fact]
    public async Task InitializeAsync_RunningTasks_MarkedInterrupted()
    {
        var store = new MemoryTransferStore();
        var running = TransferTaskFactory.Create(TransferStatus.Running);
        await store.SaveAsync(running);
        var initializer = new TransferRuntimeInitializer(store);

        var result = await initializer.InitializeAsync();

        Assert.True(result.IsSuccess);
        var interrupted = store.Tasks.Single(t => t.Id == running.Id);
        Assert.Equal(TransferStatus.Interrupted, interrupted.Status);
        Assert.True(interrupted.IsRetryable);
        Assert.NotNull(interrupted.StatusReason);
        Assert.Contains("上次退出", interrupted.StatusReason);
    }

    [Fact]
    public async Task InitializeAsync_MixedRunningAndPending()
    {
        var store = new MemoryTransferStore();
        var running = TransferTaskFactory.Create(TransferStatus.Running);
        var pending = TransferTaskFactory.Create(TransferStatus.Pending);
        await store.SaveAsync(running);
        await store.SaveAsync(pending);
        var initializer = new TransferRuntimeInitializer(store);

        var result = await initializer.InitializeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(TransferStatus.Interrupted, store.Tasks.Single(t => t.Id == running.Id).Status);
        Assert.Equal(TransferStatus.Pending, store.Tasks.Single(t => t.Id == pending.Id).Status);
    }

    [Fact]
    public async Task InitializeAsync_StoreUnavailable_ReturnsError()
    {
        var store = new FailingTaskStore();
        var initializer = new TransferRuntimeInitializer(store);

        var result = await initializer.InitializeAsync();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task InitializeAsync_Idempotent_SecondCallNoOp()
    {
        var store = new MemoryTransferStore();
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Pending));
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Succeeded));
        var initializer = new TransferRuntimeInitializer(store);

        var first = await initializer.InitializeAsync();
        var second = await initializer.InitializeAsync();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.DoesNotContain(store.Tasks, t => t.Status == TransferStatus.Interrupted);
    }

    [Fact]
    public async Task InitializeAsync_InterruptedTasks_HaveRetryableError()
    {
        var store = new MemoryTransferStore();
        var running = TransferTaskFactory.Create(TransferStatus.Running);
        await store.SaveAsync(running);
        var initializer = new TransferRuntimeInitializer(store);

        var result = await initializer.InitializeAsync();

        Assert.True(result.IsSuccess);
        var interrupted = store.Tasks.Single(t => t.Id == running.Id);
        Assert.Equal(TransferStatus.Interrupted, interrupted.Status);
        Assert.Equal(StorageErrorCategory.Unknown, interrupted.ErrorCategory);
        Assert.True(interrupted.IsRetryable);
    }

    private sealed class FailingTaskStore : ITransferTaskStore
    {
        private static readonly StorageError Error = new(
            StorageErrorCode.InfrastructureUnavailable, "store down", StorageErrorCategory.Infrastructure);

        public Task<OperationResult<TransferTask>> GetByIdAsync(
            TransferTaskId taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<TransferTask>.Failure(Error));

        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<TransferTask>>.Failure(Error));

        public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure(Error));

        public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure(Error));
    }
}
