namespace AtomBox.Core.Errors;

public enum StorageErrorCode
{
    Unknown = 0,
    ValidationFailed = 1,
    NotFound = 2,
    AuthenticationFailed = 3,
    AuthorizationFailed = 4,
    Conflict = 5,
    NetworkUnavailable = 6,
    ProviderUnavailable = 7,
    InfrastructureUnavailable = 8,
    OperationCanceled = 9,
    OperationNotSupported = 10
}
