using AtomBox.Infrastructure.Configuration;

namespace AtomBox.Infrastructure.Caching;

public sealed class ThumbnailCache
{
    private readonly AtomBoxStoragePaths _paths;

    public ThumbnailCache(AtomBoxStoragePaths paths)
    {
        _paths = paths;
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Path.Combine(_paths.CacheDirectory, "thumbnails"));
    }
}
