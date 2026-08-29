using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Transfers;

public sealed class LocalTransferWriteHandle : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly Func<CancellationToken, Task<OperationResult>> _commitAsync;
    private readonly Func<CancellationToken, Task<OperationResult>> _abortAsync;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _streamDisposed;
    private WriteState _state;

    public LocalTransferWriteHandle(Stream stream)
        : this(stream, default)
    {
    }

    public LocalTransferWriteHandle(Stream stream, LocalPath path)
        : this(
            stream,
            path,
            _ => Task.FromResult(OperationResult.Success()),
            _ => Task.FromResult(OperationResult.Success()))
    {
    }

    public LocalTransferWriteHandle(
        Stream stream,
        LocalPath path,
        Func<CancellationToken, Task<OperationResult>> commitAsync,
        Func<CancellationToken, Task<OperationResult>> abortAsync)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _commitAsync = commitAsync ?? throw new ArgumentNullException(nameof(commitAsync));
        _abortAsync = abortAsync ?? throw new ArgumentNullException(nameof(abortAsync));
        if (!stream.CanWrite)
        {
            throw new ArgumentException("Local transfer write stream must be writable.", nameof(stream));
        }

        Stream = stream;
        Path = path;
    }

    public Stream Stream { get; }

    public LocalPath Path { get; }

    public async Task<OperationResult> CommitAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == WriteState.Committed)
            {
                return OperationResult.Success();
            }

            if (_state == WriteState.Aborted)
            {
                return OperationResult.Failure(new StorageError(
                    StorageErrorCode.Conflict,
                    "Local transfer write was already aborted.",
                    StorageErrorCategory.Conflict));
            }

            var disposeResult = await DisposeStreamAsync().ConfigureAwait(false);
            if (disposeResult.IsFailure)
            {
                return disposeResult;
            }

            var commitResult = await _commitAsync(cancellationToken).ConfigureAwait(false);
            if (commitResult.IsSuccess)
            {
                _state = WriteState.Committed;
            }

            return commitResult;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResult> AbortAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state is WriteState.Committed or WriteState.Aborted)
            {
                return OperationResult.Success();
            }

            var disposeResult = await DisposeStreamAsync().ConfigureAwait(false);
            var abortResult = await _abortAsync(cancellationToken).ConfigureAwait(false);
            if (abortResult.IsSuccess)
            {
                _state = WriteState.Aborted;
            }

            return disposeResult.IsFailure ? disposeResult : abortResult;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await AbortAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<OperationResult> DisposeStreamAsync()
    {
        if (_streamDisposed)
        {
            return OperationResult.Success();
        }

        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _streamDisposed = true;
            return OperationResult.Success();
        }
        catch (Exception exception)
        {
            return OperationResult.Failure(StorageError.Unknown(
                $"Local transfer stream could not be closed: {exception.GetType().Name}."));
        }
    }

    private enum WriteState
    {
        Open,
        Committed,
        Aborted
    }
}
