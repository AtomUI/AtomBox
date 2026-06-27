namespace AtomBox.Providers.ObjectStorage;

internal static class ObjectStorageUploadPolicy
{
    public const long MultipartThreshold = 5L * 1024L * 1024L;

    public const long PartSize = 5L * 1024L * 1024L;

    public static bool ShouldUseMultipart(long? contentLength)
    {
        return contentLength is >= MultipartThreshold;
    }
}
