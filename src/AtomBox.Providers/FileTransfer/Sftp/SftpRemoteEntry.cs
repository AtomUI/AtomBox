namespace AtomBox.Providers.FileTransfer.Sftp;

internal sealed record SftpRemoteEntry(
    string Name,
    bool IsDirectory,
    bool IsRegularFile,
    long Length,
    DateTime LastWriteTime);
