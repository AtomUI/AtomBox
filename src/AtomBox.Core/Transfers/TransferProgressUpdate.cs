using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Transfers;

public sealed record TransferProgressUpdate
{
    public TransferProgressUpdate(
        TransferTaskId taskId,
        long executionRevision,
        long sequence,
        TransferProgress progress)
    {
        if (taskId.IsEmpty)
        {
            throw new ArgumentException("Transfer task id cannot be empty.", nameof(taskId));
        }

        if (executionRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(executionRevision),
                "Execution revision cannot be negative.");
        }

        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Progress sequence cannot be negative.");
        }

        TaskId = taskId;
        ExecutionRevision = executionRevision;
        Sequence = sequence;
        Progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    public TransferTaskId TaskId { get; }

    public long ExecutionRevision { get; }

    public long Sequence { get; }

    public TransferProgress Progress { get; }
}
