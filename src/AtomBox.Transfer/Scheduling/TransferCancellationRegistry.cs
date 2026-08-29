using System.Collections.Concurrent;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Transfer.Scheduling;

public interface ITransferCancellationState
{
    bool IsUserCancellationRequested(TransferTaskId taskId);
}

public sealed class TransferCancellationRegistry : ITransferCancellationState
{
    private readonly ConcurrentDictionary<TransferTaskId, RunningTransferCancellation> _runningTasks = [];

    public RunningTransferCancellationLease Register(
        TransferTaskId taskId,
        CancellationToken cancellationToken)
    {
        var cancellation = new RunningTransferCancellation(cancellationToken);
        if (!_runningTasks.TryAdd(taskId, cancellation))
        {
            cancellation.Dispose();
            throw new InvalidOperationException($"Transfer task {taskId} is already registered as running.");
        }

        return new RunningTransferCancellationLease(taskId, cancellation.Token, this);
    }

    public bool Cancel(TransferTaskId taskId)
    {
        if (!_runningTasks.TryGetValue(taskId, out var cancellation))
        {
            return false;
        }

        return cancellation.CancelByUser();
    }

    public bool IsUserCancellationRequested(TransferTaskId taskId)
    {
        return _runningTasks.TryGetValue(taskId, out var cancellation) &&
               cancellation.IsUserCancellationRequested;
    }

    private void Unregister(TransferTaskId taskId)
    {
        if (_runningTasks.TryRemove(taskId, out var cancellation))
        {
            cancellation.Dispose();
        }
    }

    private sealed class RunningTransferCancellation : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly object _gate = new();
        private int _isUserCancellationRequested;
        private bool _disposed;

        public RunningTransferCancellation(CancellationToken cancellationToken)
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        public CancellationToken Token => _cancellationTokenSource.Token;

        public bool IsUserCancellationRequested => Volatile.Read(ref _isUserCancellationRequested) == 1;

        public bool CancelByUser()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return false;
                }

                Interlocked.Exchange(ref _isUserCancellationRequested, 1);
                _cancellationTokenSource.Cancel();
                return true;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _cancellationTokenSource.Dispose();
            }
        }
    }

    public sealed class RunningTransferCancellationLease : IDisposable
    {
        private readonly TransferCancellationRegistry _registry;
        private bool _disposed;

        internal RunningTransferCancellationLease(
            TransferTaskId taskId,
            CancellationToken token,
            TransferCancellationRegistry registry)
        {
            TaskId = taskId;
            Token = token;
            _registry = registry;
        }

        public TransferTaskId TaskId { get; }

        public CancellationToken Token { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _registry.Unregister(TaskId);
        }
    }
}

internal sealed class NoTransferCancellationState : ITransferCancellationState
{
    public static NoTransferCancellationState Instance { get; } = new();

    private NoTransferCancellationState()
    {
    }

    public bool IsUserCancellationRequested(TransferTaskId taskId)
    {
        return false;
    }
}
