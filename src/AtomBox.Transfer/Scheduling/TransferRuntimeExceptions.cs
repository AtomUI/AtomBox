using AtomBox.Core.Errors;

namespace AtomBox.Transfer.Scheduling;

public sealed class TransferRuntimeStartupException : Exception
{
    public TransferRuntimeStartupException(StorageError error, Exception? innerException = null)
        : base(error?.Message, innerException)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public StorageError Error { get; }
}

public sealed class TransferRuntimeShutdownException : Exception
{
    public TransferRuntimeShutdownException(IReadOnlyList<Exception> failures)
        : base(
            "One or more failures occurred while stopping the transfer runtime.",
            new AggregateException(failures ?? throw new ArgumentNullException(nameof(failures))))
    {
        Failures = Array.AsReadOnly(failures.ToArray());
    }

    public IReadOnlyList<Exception> Failures { get; }
}

internal sealed class TransferRuntimePersistenceException : Exception
{
    public TransferRuntimePersistenceException(StorageError error)
        : base(error?.Message)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public StorageError Error { get; }
}
