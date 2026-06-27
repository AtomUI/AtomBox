using AtomBox.Infrastructure.Configuration;

namespace AtomBox.Infrastructure.Caching;

public sealed class MetadataCache
{
    private readonly AtomBoxStoragePaths _paths;

    public MetadataCache(AtomBoxStoragePaths paths)
    {
        _paths = paths;
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Path.Combine(_paths.CacheDirectory, "metadata"));
    }
}
