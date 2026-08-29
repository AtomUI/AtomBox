namespace AtomBox.Providers.FileTransfer.WebDav;

internal interface IWebDavClientAdapter : IDisposable
{
    Task<IReadOnlyList<WebDavRemoteEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken);

    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken);

    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken);

    Task DeleteAsync(string path, CancellationToken cancellationToken);

    Task MoveAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken);

    Task UploadFileAsync(Stream content, string path, CancellationToken cancellationToken);

    Task DownloadFileAsync(string path, Stream destination, CancellationToken cancellationToken);
}
