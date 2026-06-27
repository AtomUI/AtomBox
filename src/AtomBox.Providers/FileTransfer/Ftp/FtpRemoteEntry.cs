namespace AtomBox.Providers.FileTransfer.Ftp;

internal sealed record FtpRemoteEntry(
    string Name,
    bool IsDirectory,
    bool IsFile,
    long Size,
    DateTime Modified);
