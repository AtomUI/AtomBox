using AtomBox.Core.Errors;
using AtomBox.Core.Results;

namespace AtomBox.Infrastructure.Configuration;

public sealed class AppConfigurationStore
{
    private readonly AtomBoxStoragePaths _paths;

    public AppConfigurationStore(AtomBoxStoragePaths paths)
    {
        _paths = paths;
    }

    public OperationResult EnsureCreated()
    {
        try
        {
            Directory.CreateDirectory(_paths.RootDirectory);
            Directory.CreateDirectory(_paths.ConfigurationDirectory);
            Directory.CreateDirectory(_paths.StateDirectory);
            Directory.CreateDirectory(_paths.CredentialDirectory);
            Directory.CreateDirectory(_paths.CacheDirectory);
            Directory.CreateDirectory(_paths.LogDirectory);
            Directory.CreateDirectory(Path.Combine(_paths.CacheDirectory, "metadata"));
            Directory.CreateDirectory(Path.Combine(_paths.CacheDirectory, "thumbnails"));

            return OperationResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure(new StorageError(
                StorageErrorCode.InfrastructureUnavailable,
                "Unable to initialize AtomBox local storage directories.",
                StorageErrorCategory.Infrastructure));
        }
    }
}
