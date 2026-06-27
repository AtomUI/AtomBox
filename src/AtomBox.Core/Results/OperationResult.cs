using AtomBox.Core.Errors;

namespace AtomBox.Core.Results;

public sealed record OperationResult
{
    private OperationResult(bool isSuccess, StorageError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public StorageError? Error { get; }

    public static OperationResult Success()
    {
        return new OperationResult(true, null);
    }

    public static OperationResult Failure(StorageError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new OperationResult(false, error);
    }
}
