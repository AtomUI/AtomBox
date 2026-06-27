namespace AtomBox.Core.Errors;

public sealed record StorageError
{
    public StorageError(
        StorageErrorCode code,
        string message,
        StorageErrorCategory category,
        bool isRetryable = false,
        string? providerErrorCode = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Storage error message cannot be empty.", nameof(message));
        }

        Code = code;
        Message = message.Trim();
        Category = category;
        IsRetryable = isRetryable;
        ProviderErrorCode = providerErrorCode;
    }

    public StorageErrorCode Code { get; }

    public string Message { get; }

    public StorageErrorCategory Category { get; }

    public bool IsRetryable { get; }

    public string? ProviderErrorCode { get; }

    public static StorageError Validation(string message)
    {
        return new StorageError(StorageErrorCode.ValidationFailed, message, StorageErrorCategory.Validation);
    }

    public static StorageError NotFound(string message)
    {
        return new StorageError(StorageErrorCode.NotFound, message, StorageErrorCategory.NotFound);
    }

    public static StorageError Unknown(string message)
    {
        return new StorageError(StorageErrorCode.Unknown, message, StorageErrorCategory.Unknown, isRetryable: false);
    }

    public static StorageError NotSupported(string message)
    {
        return new StorageError(StorageErrorCode.OperationNotSupported, message, StorageErrorCategory.Validation);
    }
}
