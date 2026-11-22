namespace AtomBox.Providers.FileTransfer.WebDav;

internal interface IWebDavClientAdapter : IAsyncDisposable
{
    Task<IReadOnlyList<WebDavRemoteEntry>> ListDirectoryAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);

    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

    Task DeleteAsync(string path, CancellationToken cancellationToken = default);

    Task MoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task UploadFileAsync(
        Stream content,
        string path,
        Action<long>? progress = null,
        CancellationToken cancellationToken = default);

    Task DownloadFileAsync(
        string path,
        Stream destination,
        Action<long>? progress = null,
        CancellationToken cancellationToken = default);
}
