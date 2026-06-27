namespace AtomBox.Core.Transfers;

public enum TransferStatus
{
    Pending = 0,
    Running = 1,
    Paused = 2,
    Interrupted = 3,
    Succeeded = 4,
    Failed = 5,
    Canceled = 6
}
