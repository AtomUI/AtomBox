namespace AtomBox.Providers.Common;

internal sealed class ProviderOperationCanceledException : OperationCanceledException
{
    public ProviderOperationCanceledException(
        string message,
        Exception innerException,
        CancellationToken cancellationToken)
        : base(message, innerException, cancellationToken)
    {
    }
}
