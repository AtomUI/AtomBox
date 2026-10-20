namespace AtomBox.Core.Providers;

public enum TransferIoExecutionKind
{
    Unknown = 0,
    Asynchronous = 1,
    Blocking = 2,
}

public sealed record TransferExecutionCapabilities
{
    public TransferExecutionCapabilities(
        TransferIoExecutionKind uploadExecution,
        TransferIoExecutionKind downloadExecution,
        bool supportsCooperativeCancellation,
        bool supportsActiveAbort,
        bool supportsProgress)
    {
        if (!Enum.IsDefined(uploadExecution))
        {
            throw new ArgumentOutOfRangeException(nameof(uploadExecution));
        }

        if (!Enum.IsDefined(downloadExecution))
        {
            throw new ArgumentOutOfRangeException(nameof(downloadExecution));
        }

        UploadExecution = uploadExecution;
        DownloadExecution = downloadExecution;
        SupportsCooperativeCancellation = supportsCooperativeCancellation;
        SupportsActiveAbort = supportsActiveAbort;
        SupportsProgress = supportsProgress;
    }

    public TransferIoExecutionKind UploadExecution { get; }

    public TransferIoExecutionKind DownloadExecution { get; }

    public bool SupportsCooperativeCancellation { get; }

    public bool SupportsActiveAbort { get; }

    public bool SupportsProgress { get; }

    public static TransferExecutionCapabilities Conservative { get; } = new(
        TransferIoExecutionKind.Unknown,
        TransferIoExecutionKind.Unknown,
        supportsCooperativeCancellation: false,
        supportsActiveAbort: false,
        supportsProgress: false);
}
