namespace AtomBox.Core.Previews;

public sealed record RemotePreviewOptions(long MaxTextBytes, long MaxImageBytes)
{
    public const long DefaultMaxTextBytes = 1 * 1024 * 1024;
    public const long DefaultMaxImageBytes = 10 * 1024 * 1024;

    public static RemotePreviewOptions Default { get; } = new(DefaultMaxTextBytes, DefaultMaxImageBytes);
}
