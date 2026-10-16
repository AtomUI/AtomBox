using AtomBox.Core.Accounts;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Transfer.Workers;

public sealed record TransferOperationExecution(
    OperationResult Result,
    LocalPath EffectiveLocalPath);

public sealed class TransferOperationExecutor
{
    private readonly IStorageAccountRepository _accounts;
    private readonly IStorageProviderFactory _providerFactory;
    private readonly ILocalTransferFileStore _localFiles;
    private readonly object _providerGate = new();
    private IStorageProvider? _activeProvider;

    public TransferOperationExecutor(
        IStorageAccountRepository accounts,
        IStorageProviderFactory providerFactory,
        ILocalTransferFileStore localFiles)
    {
        _accounts = accounts;
        _providerFactory = providerFactory;
        _localFiles = localFiles;
    }

    public async Task<TransferOperationExecution> ExecuteAsync(
        TransferTask runningTask,
        IProgress<TransferProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runningTask);
        ArgumentNullException.ThrowIfNull(progress);

        var accountResult = await _accounts
            .GetByIdAsync(runningTask.StorageAccountId, cancellationToken)
            .ConfigureAwait(false);
        if (accountResult.IsFailure)
        {
            return new TransferOperationExecution(
                OperationResult.Failure(accountResult.Error!),
                runningTask.LocalPath);
        }

        var providerResult = await _providerFactory
            .CreateAsync(accountResult.GetValueOrThrow(), cancellationToken)
            .ConfigureAwait(false);
        if (providerResult.IsFailure)
        {
            return new TransferOperationExecution(
                OperationResult.Failure(providerResult.Error!),
                runningTask.LocalPath);
        }

        await using var provider = providerResult.GetValueOrThrow();
        lock (_providerGate)
        {
            _activeProvider = provider;
        }

        try
        {
            return runningTask.Direction switch
            {
                TransferDirection.Upload => await UploadAsync(
                    provider,
                    runningTask,
                    progress,
                    cancellationToken).ConfigureAwait(false),
                TransferDirection.Download => await DownloadAsync(
                    provider,
                    runningTask,
                    progress,
                    cancellationToken).ConfigureAwait(false),
                _ => new TransferOperationExecution(
                    OperationResult.Failure(AtomBox.Core.Errors.StorageError.Validation(
                        "Unsupported transfer direction.")),
                    runningTask.LocalPath),
            };
        }
        finally
        {
            lock (_providerGate)
            {
                if (ReferenceEquals(_activeProvider, provider))
                {
                    _activeProvider = null;
                }
            }
        }
    }

    public async ValueTask<OperationResult> TryAbortAsync(CancellationToken cancellationToken = default)
    {
        IStorageProvider? provider;
        lock (_providerGate)
        {
            provider = _activeProvider;
        }

        if (provider is null || !provider.TransferCapabilities.SupportsActiveAbort)
        {
            return OperationResult.Success();
        }

        return await provider.TryAbortTransferAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<TransferOperationExecution> UploadAsync(
        IStorageProvider provider,
        TransferTask task,
        IProgress<TransferProgress> progress,
        CancellationToken cancellationToken)
    {
        var fileResult = await _localFiles.OpenReadAsync(task.LocalPath, cancellationToken)
            .ConfigureAwait(false);
        if (fileResult.IsFailure)
        {
            return new TransferOperationExecution(
                OperationResult.Failure(fileResult.Error!),
                task.LocalPath);
        }

        await using var file = fileResult.GetValueOrThrow();
        var result = await InvokeProviderAsync(
            provider.TransferCapabilities.UploadExecution,
            () => provider.UploadAsync(
                task.RemotePath,
                file.Stream,
                file.Length,
                progress,
                cancellationToken)).ConfigureAwait(false);
        return new TransferOperationExecution(result, task.LocalPath);
    }

    private async Task<TransferOperationExecution> DownloadAsync(
        IStorageProvider provider,
        TransferTask task,
        IProgress<TransferProgress> progress,
        CancellationToken cancellationToken)
    {
        var fileResult = await _localFiles
            .OpenWriteAsync(task.LocalPath, task.Options.OverwritePolicy, cancellationToken)
            .ConfigureAwait(false);
        if (fileResult.IsFailure)
        {
            return new TransferOperationExecution(
                OperationResult.Failure(fileResult.Error!),
                task.LocalPath);
        }

        await using var file = fileResult.GetValueOrThrow();
        var result = await InvokeProviderAsync(
            provider.TransferCapabilities.DownloadExecution,
            () => provider.DownloadAsync(
                task.RemotePath,
                file.Stream,
                progress,
                cancellationToken)).ConfigureAwait(false);
        return new TransferOperationExecution(result, file.Path);
    }

    private static Task<OperationResult> InvokeProviderAsync(
        TransferIoExecutionKind executionKind,
        Func<Task<OperationResult>> operation)
    {
        return executionKind == TransferIoExecutionKind.Asynchronous
            ? operation()
            : Task.Run(operation);
    }
}
