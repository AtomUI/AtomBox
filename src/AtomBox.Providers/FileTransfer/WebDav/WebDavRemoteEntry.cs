namespace AtomBox.Providers.FileTransfer.WebDav;

internal sealed record WebDavRemoteEntry(
    string Name,
    bool IsDirectory,
    long? Length,
    DateTimeOffset? LastModified);
