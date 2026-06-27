namespace AtomBox.Providers.ObjectStorage;

internal static class ObjectStorageSearchPrefix
{
    public static string Combine(string folderPrefix, string? searchPrefix)
    {
        var trimmed = searchPrefix?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return folderPrefix;
        }

        var normalizedFolderPrefix = folderPrefix ?? string.Empty;
        return normalizedFolderPrefix + trimmed.TrimStart('/');
    }
}
