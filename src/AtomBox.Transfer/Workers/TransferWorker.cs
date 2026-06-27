using AtomBox.Core.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Transfer.Progress;
using AtomBox.Transfer.Scheduling;

namespace AtomBox.Transfer.Workers;

public sealed class TransferWorker
{
    private readonly IStorageAccountRepository _accounts;
    private readonly IStorageProviderFactory _providerFactory;
    private readonly ILocalTransferFileStore _localFiles;
    private readonly ITransferStateStore _stateStore;
    private readonly ITransferCancellationState _cancellationState;

    public TransferWorker(
        IStorageAccountRepository accounts,
        IStorageProviderFactory providerFactory,
        ILocalTransferFileStore localFiles,
        ITransferStateStore stateStore,
        ITransferCancellationState? cancellationState = null)
    {
        _accounts = accounts;
        _providerFactory = providerFactory;
        _localFiles = localFiles;
        _stateStore = stateStore;
        _cancellationState = cancellationState ?? NoTransferCancellationState.Instance;
    }

    public async Task<OperationResult> ExecuteAsync(
        TransferTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        var now = DateTimeOffset.UtcNow;
        var running = task.WithStatus(TransferStatus.Running, now);
        var runningResult = await _stateStore.UpdateStatusAsync(
            running,
            new TransferProgress(0, null, null),
            cancellationToken).ConfigureAwait(false);

        if (runningResult.IsFailure)
        {
            return runningResult;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return await MarkCanceledOrInterruptedAsync(
                running,
                progress: null,
                "传输任务在启动后被中断。",
                CancellationToken.None).ConfigureAwait(false);
        }

        var progress = new TransferProgressSink(_stateStore, running, cancellationToken);
        OperationResult executeResult;
        try
        {
            executeResult = await ExecuteTransferAsync(running, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await MarkCanceledOrInterruptedAsync(
                progress.CurrentTask,
                progress.Latest,
                "传输任务被应用关闭或取消令牌中断，远端最终状态未知。",
                CancellationToken.None).ConfigureAwait(false);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return await MarkCanceledOrInterruptedAsync(
                progress.CurrentTask,
                progress.Latest,
                "传输任务在执行完成前被中断。",
                CancellationToken.None).ConfigureAwait(false);
        }

        if (executeResult.IsFailure)
        {
            if (_cancellationState.IsUserCancellationRequested(progress.CurrentTask.Id))
            {
                return await MarkCanceledAsync(progress.CurrentTask, progress.Latest, CancellationToken.None).ConfigureAwait(false);
            }

            var failed = progress.CurrentTask.WithStatus(
                TransferStatus.Failed,
                DateTimeOffset.UtcNow,
                executeResult.Error?.Message ?? "传输任务失败。",
                executeResult.Error?.Category ?? StorageErrorCategory.Unknown,
                executeResult.Error?.IsRetryable ?? true);
            var failedResult = await _stateStore.UpdateStatusAsync(
                failed,
                progress.Latest,
                cancellationToken).ConfigureAwait(false);

            return failedResult.IsFailure ? failedResult : executeResult;
        }

        var completed = progress.CurrentTask.WithStatus(TransferStatus.Succeeded, DateTimeOffset.UtcNow);
        return await _stateStore.UpdateStatusAsync(
            completed,
            progress.Latest ?? new TransferProgress(1, 1, null),
            cancellationToken).ConfigureAwait(false);
    }

    private Task<OperationResult> MarkCanceledOrInterruptedAsync(
        TransferTask running,
        TransferProgress? progress,
        string reason,
        CancellationToken cancellationToken)
    {
        return _cancellationState.IsUserCancellationRequested(running.Id)
            ? MarkCanceledAsync(running, progress, cancellationToken)
            : MarkInterruptedAsync(running, progress, reason, cancellationToken);
    }

    private Task<OperationResult> MarkCanceledAsync(
        TransferTask running,
        TransferProgress? progress,
        CancellationToken cancellationToken)
    {
        var canceled = running.WithStatus(
            TransferStatus.Canceled,
            DateTimeOffset.UtcNow,
            "用户取消了传输任务。");

        return _stateStore.UpdateStatusAsync(canceled, progress, cancellationToken);
    }

    private Task<OperationResult> MarkInterruptedAsync(
        TransferTask running,
        TransferProgress? progress,
        string reason,
        CancellationToken cancellationToken)
    {
        var interrupted = running.WithStatus(
            TransferStatus.Interrupted,
            DateTimeOffset.UtcNow,
            reason,
            StorageErrorCategory.Unknown,
            isRetryable: true);

        return _stateStore.UpdateStatusAsync(interrupted, progress, cancellationToken);
    }

    private async Task<OperationResult> ExecuteTransferAsync(
        TransferTask task,
        TransferProgressSink progress,
        CancellationToken cancellationToken)
    {
        var accountResult = await _accounts.GetByIdAsync(task.StorageAccountId, cancellationToken).ConfigureAwait(false);
        if (accountResult.IsFailure)
        {
            return OperationResult.Failure(accountResult.Error!);
        }

        var providerResult = await _providerFactory.CreateAsync(accountResult.GetValueOrThrow(), cancellationToken).ConfigureAwait(false);
        if (providerResult.IsFailure)
        {
            return OperationResult.Failure(providerResult.Error!);
        }

        await using var provider = providerResult.GetValueOrThrow();
        return task.Direction switch
        {
            TransferDirection.Upload => await UploadAsync(provider, task, progress, cancellationToken).ConfigureAwait(false),
            TransferDirection.Download => await DownloadAsync(provider, task, progress, cancellationToken).ConfigureAwait(false),
            _ => OperationResult.Failure(AtomBox.Core.Errors.StorageError.Validation("Unsupported transfer direction."))
        };
    }

    private async Task<OperationResult> UploadAsync(
        IStorageProvider provider,
        TransferTask task,
        TransferProgressSink progress,
        CancellationToken cancellationToken)
    {
        var fileResult = await _localFiles.OpenReadAsync(task.LocalPath, cancellationToken).ConfigureAwait(false);
        if (fileResult.IsFailure)
        {
            return OperationResult.Failure(fileResult.Error!);
        }

        await using var file = fileResult.GetValueOrThrow();
        return await provider.UploadAsync(
            task.RemotePath,
            file.Stream,
            file.Length,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult> DownloadAsync(
        IStorageProvider provider,
        TransferTask task,
        TransferProgressSink progress,
        CancellationToken cancellationToken)
    {
        var fileResult = await _localFiles
            .OpenWriteAsync(task.LocalPath, task.Options.OverwritePolicy, cancellationToken)
            .ConfigureAwait(false);
        if (fileResult.IsFailure)
        {
            return OperationResult.Failure(fileResult.Error!);
        }

        await using var file = fileResult.GetValueOrThrow();
        if (!file.Path.IsEmpty && file.Path != task.LocalPath)
        {
            var renamedTask = task.WithLocalPath(file.Path, DateTimeOffset.UtcNow);
            var updateResult = await _stateStore
                .UpdateStatusAsync(renamedTask, progress.Latest, cancellationToken)
                .ConfigureAwait(false);
            if (updateResult.IsFailure)
            {
                return updateResult;
            }

            progress.UpdateTask(renamedTask);
        }

        return await provider.DownloadAsync(
            task.RemotePath,
            file.Stream,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    private sealed class TransferProgressSink : IProgress<TransferProgress>
    {
        private readonly ITransferStateStore _stateStore;
        private TransferTask _runningTask;
        private readonly CancellationToken _cancellationToken;
        private readonly TransferSpeedMeter _speedMeter = new();
        private DateTimeOffset _lastPersistedAt = DateTimeOffset.MinValue;

        public TransferProgressSink(
            ITransferStateStore stateStore,
            TransferTask runningTask,
            CancellationToken cancellationToken)
        {
            _stateStore = stateStore;
            _runningTask = runningTask;
            _cancellationToken = cancellationToken;
        }

        public TransferProgress? Latest { get; private set; }

        public TransferTask CurrentTask => _runningTask;

        public void UpdateTask(TransferTask task)
        {
            _runningTask = task ?? throw new ArgumentNullException(nameof(task));
        }

        public void Report(TransferProgress value)
        {
            Latest = _speedMeter.Apply(value);
            PersistLatestIfNeeded();
        }

        private void PersistLatestIfNeeded()
        {
            if (Latest is null || _cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var isFinal = Latest.TotalBytes is not null && Latest.BytesTransferred >= Latest.TotalBytes.Value;
            if (!isFinal && (now - _lastPersistedAt) < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            _lastPersistedAt = now;
            _stateStore.UpdateStatusAsync(_runningTask, Latest, _cancellationToken)
                .GetAwaiter()
                .GetResult();
        }
    }
}
