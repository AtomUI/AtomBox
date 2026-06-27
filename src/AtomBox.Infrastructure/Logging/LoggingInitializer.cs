using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Infrastructure.Configuration;

namespace AtomBox.Infrastructure.Logging;

public sealed class LoggingInitializer
{
    private readonly AtomBoxStoragePaths _paths;

    public LoggingInitializer(AtomBoxStoragePaths paths)
    {
        _paths = paths;
    }

    public OperationResult Initialize()
    {
        try
        {
            Directory.CreateDirectory(_paths.LogDirectory);
            return OperationResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure(new StorageError(
                StorageErrorCode.InfrastructureUnavailable,
                "Unable to initialize AtomBox logging directory.",
                StorageErrorCategory.Infrastructure));
        }
    }

    public void Flush()
    {
    }
}
