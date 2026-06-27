using AtomBox.Core.Errors;
using System.Diagnostics.CodeAnalysis;

namespace AtomBox.Core.Results;

public sealed record OperationResult<T>
{
    private OperationResult(bool isSuccess, T? value, StorageError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public T? Value { get; }

    public StorageError? Error { get; }

    public static OperationResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new OperationResult<T>(true, value, null);
    }

    public static OperationResult<T> Failure(StorageError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new OperationResult<T>(false, default, error);
    }

    public T GetValueOrThrow()
    {
        if (IsFailure)
        {
            throw new InvalidOperationException($"Cannot read value from failed operation: {Error?.Message}");
        }

        return Value ?? throw new InvalidOperationException("Cannot read null value from successful operation.");
    }

    public bool TryGetValue([NotNullWhen(true)] out T? value)
    {
        value = Value;
        return IsSuccess && value is not null;
    }
}
