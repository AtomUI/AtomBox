using AtomBox.Core.Transfers;

namespace AtomBox.Transfer.Queue;

public sealed class TransferQueue
{
    public IReadOnlyList<TransferTask> SelectPending(IReadOnlyList<TransferTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        return tasks
            .Where(task => task.Status is TransferStatus.Pending)
            .OrderBy(task => task.CreatedAt)
            .ToArray();
    }
}
