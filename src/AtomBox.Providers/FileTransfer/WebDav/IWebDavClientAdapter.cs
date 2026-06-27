namespace AtomBox.Providers.FileTransfer.WebDav;

internal interface IWebDavClientAdapter : IDisposable
{
    IReadOnlyList<WebDavRemoteEntry> ListDirectory(string path);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    void Delete(string path);

    void Move(string sourcePath, string destinationPath);

    void UploadFile(Stream content, string path, Action<long>? progress = null);

    void DownloadFile(string path, Stream destination, Action<long>? progress = null);
}
