using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtomBox.Providers.NetDisk.BaiduNetDisk;

internal sealed class BaiduNetDiskHttpClient : IBaiduNetDiskClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _apiClient;
    private readonly HttpClient _contentClient;
    private readonly string _accessToken;

    public BaiduNetDiskHttpClient(string apiEndpoint, string contentEndpoint, string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("Access token is required.", nameof(accessToken));
        }

        _apiClient = new HttpClient { BaseAddress = new Uri(NormalizeEndpoint(apiEndpoint)) };
        _contentClient = new HttpClient { BaseAddress = new Uri(NormalizeEndpoint(contentEndpoint)) };
        _accessToken = accessToken;
    }

    public async Task<IReadOnlyList<BaiduNetDiskFileItem>> ListAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var requestUri = BuildApiUri(
            "/rest/2.0/xpan/file",
            ("method", "list"),
            ("dir", directoryPath),
            ("web", "1"),
            ("page", "1"),
            ("num", "1000"));
        using var response = await _apiClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        var payload = await ReadPayloadAsync<BaiduListResponse>(response, cancellationToken).ConfigureAwait(false);
        ThrowIfErrno(payload.Errno);

        return (payload.List ?? [])
            .Select(item => new BaiduNetDiskFileItem(
                item.Path ?? "/" + (item.ServerFilename ?? string.Empty),
                item.ServerFilename ?? Path.GetFileName(item.Path ?? string.Empty),
                item.IsDir == 1,
                item.IsDir == 1 ? null : item.Size,
                UnixSecondsToDateTimeOffset(item.ServerMTime)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray();
    }

    public async Task DeleteAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var requestUri = BuildApiUri(
            "/rest/2.0/xpan/file",
            ("method", "filemanager"),
            ("opera", "delete"));
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("async", "0"),
            new KeyValuePair<string, string>("filelist", JsonSerializer.Serialize(new[] { path }, JsonOptions))
        ]);
        using var response = await _apiClient.PostAsync(requestUri, content, cancellationToken).ConfigureAwait(false);
        var payload = await ReadPayloadAsync<BaiduErrnoResponse>(response, cancellationToken).ConfigureAwait(false);
        ThrowIfErrno(payload.Errno);
    }

    public async Task UploadAsync(
        string path,
        Stream content,
        long? contentLength,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var uploadContent = await PrepareUploadContentAsync(content, contentLength, cancellationToken)
            .ConfigureAwait(false);
        var length = uploadContent.Length;
        var blockMd5 = ComputeMd5(uploadContent.Content);
        var blockListJson = JsonSerializer.Serialize(new[] { blockMd5 }, JsonOptions);

        var precreate = await PrecreateAsync(path, length, blockListJson, cancellationToken).ConfigureAwait(false);
        var uploadId = precreate.UploadId;
        if (string.IsNullOrWhiteSpace(uploadId))
        {
            throw new BaiduNetDiskApiException(precreate.Errno);
        }

        uploadContent.Content.Position = uploadContent.StartPosition;
        await UploadBlockAsync(path, uploadId, uploadContent.Content, length, progress, cancellationToken)
            .ConfigureAwait(false);
        await CreateAsync(path, length, uploadId, blockListJson, cancellationToken).ConfigureAwait(false);
        progress?.Report(length);
    }

    public async Task<BaiduNetDiskDownloadObject> GetObjectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var requestUri = BuildContentUri(
            "/rest/2.0/pcs/file",
            ("method", "download"),
            ("path", path));
        var response = await _contentClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        try
        {
            await ThrowIfHttpErrorAsync(response, cancellationToken).ConfigureAwait(false);
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new BaiduNetDiskDownloadObject(stream, response.Content.Headers.ContentLength, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _apiClient.Dispose();
        _contentClient.Dispose();
    }

    private async Task<BaiduPrecreateResponse> PrecreateAsync(
        string path,
        long size,
        string blockListJson,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildApiUri("/rest/2.0/xpan/file", ("method", "precreate"));
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("path", path),
            new KeyValuePair<string, string>("size", size.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("isdir", "0"),
            new KeyValuePair<string, string>("autoinit", "1"),
            new KeyValuePair<string, string>("rtype", "3"),
            new KeyValuePair<string, string>("block_list", blockListJson)
        ]);
        using var response = await _apiClient.PostAsync(requestUri, content, cancellationToken).ConfigureAwait(false);
        var payload = await ReadPayloadAsync<BaiduPrecreateResponse>(response, cancellationToken).ConfigureAwait(false);
        ThrowIfErrno(payload.Errno);
        return payload;
    }

    private async Task UploadBlockAsync(
        string path,
        string uploadId,
        Stream content,
        long contentLength,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildContentUri(
            "/rest/2.0/pcs/superfile2",
            ("method", "upload"),
            ("type", "tmpfile"),
            ("path", path),
            ("uploadid", uploadId),
            ("partseq", "0"));
        using var multipart = new MultipartFormDataContent();
        using var streamContent = new ProgressStreamContent(content, contentLength, progress, cancellationToken);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(streamContent, "file", Path.GetFileName(path));

        using var response = await _contentClient.PostAsync(requestUri, multipart, cancellationToken).ConfigureAwait(false);
        var payload = await ReadPayloadAsync<BaiduErrnoResponse>(response, cancellationToken).ConfigureAwait(false);
        ThrowIfErrno(payload.Errno);
    }

    private async Task CreateAsync(
        string path,
        long size,
        string uploadId,
        string blockListJson,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildApiUri("/rest/2.0/xpan/file", ("method", "create"));
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("path", path),
            new KeyValuePair<string, string>("size", size.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("isdir", "0"),
            new KeyValuePair<string, string>("rtype", "3"),
            new KeyValuePair<string, string>("uploadid", uploadId),
            new KeyValuePair<string, string>("block_list", blockListJson)
        ]);
        using var response = await _apiClient.PostAsync(requestUri, content, cancellationToken).ConfigureAwait(false);
        var payload = await ReadPayloadAsync<BaiduErrnoResponse>(response, cancellationToken).ConfigureAwait(false);
        ThrowIfErrno(payload.Errno);
    }

    private string BuildApiUri(string path, params (string Name, string Value)[] values)
    {
        return BuildUri(path, values);
    }

    private string BuildContentUri(string path, params (string Name, string Value)[] values)
    {
        return BuildUri(path, values);
    }

    private string BuildUri(string path, params (string Name, string Value)[] values)
    {
        var query = values
            .Append((Name: "access_token", Value: _accessToken))
            .Select(item => $"{Uri.EscapeDataString(item.Name)}={Uri.EscapeDataString(item.Value)}");
        return path + "?" + string.Join("&", query);
    }

    private static async Task<T> ReadPayloadAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await ThrowIfHttpErrorAsync(response, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
        return payload ?? throw new InvalidOperationException("Baidu Netdisk response payload was empty.");
    }

    private static async Task ThrowIfHttpErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<BaiduErrnoResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (payload is not null && payload.Errno != 0)
        {
            throw new BaiduNetDiskApiException(payload.Errno);
        }

        throw new BaiduNetDiskApiException((int)response.StatusCode);
    }

    private static void ThrowIfErrno(int errno)
    {
        if (errno != 0)
        {
            throw new BaiduNetDiskApiException(errno);
        }
    }

    private static string NormalizeEndpoint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Endpoint is required.", nameof(value));
        }

        return value.Trim().TrimEnd('/');
    }

    private static string ComputeMd5(Stream content)
    {
        var startPosition = content.Position;
        var hash = MD5.HashData(content);
        content.Position = startPosition;
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<UploadContentLease> PrepareUploadContentAsync(
        Stream content,
        long? contentLength,
        CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            var startPosition = content.Position;
            var length = contentLength ?? content.Length - startPosition;
            return new UploadContentLease(content, length, startPosition, ownsContent: false);
        }

        var bufferedContent = new MemoryStream();
        await content.CopyToAsync(bufferedContent, cancellationToken).ConfigureAwait(false);
        bufferedContent.Position = 0;
        return new UploadContentLease(bufferedContent, bufferedContent.Length, 0, ownsContent: true);
    }

    private static DateTimeOffset? UnixSecondsToDateTimeOffset(long? value)
    {
        return value is null or <= 0 ? null : DateTimeOffset.FromUnixTimeSeconds(value.Value);
    }

    private sealed record BaiduErrnoResponse(
        [property: JsonPropertyName("errno")] int Errno);

    private sealed record BaiduListResponse(
        [property: JsonPropertyName("errno")] int Errno,
        [property: JsonPropertyName("list")] IReadOnlyList<BaiduListItem>? List);

    private sealed record BaiduListItem(
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("server_filename")] string? ServerFilename,
        [property: JsonPropertyName("isdir")] int IsDir,
        [property: JsonPropertyName("size")] long? Size,
        [property: JsonPropertyName("server_mtime")] long? ServerMTime);

    private sealed record BaiduPrecreateResponse(
        [property: JsonPropertyName("errno")] int Errno,
        [property: JsonPropertyName("uploadid")] string? UploadId);

    private sealed class ProgressStreamContent : HttpContent
    {
        private readonly Stream _source;
        private readonly long _length;
        private readonly IProgress<long>? _progress;
        private readonly CancellationToken _cancellationToken;

        public ProgressStreamContent(
            Stream source,
            long length,
            IProgress<long>? progress,
            CancellationToken cancellationToken)
        {
            _source = source;
            _length = length;
            _progress = progress;
            _cancellationToken = cancellationToken;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buffer = new byte[81920];
            long transferred = 0;
            while (true)
            {
                var read = await _source.ReadAsync(buffer, _cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await stream.WriteAsync(buffer.AsMemory(0, read), _cancellationToken).ConfigureAwait(false);
                transferred += read;
                _progress?.Report(transferred);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }

    private sealed class UploadContentLease : IDisposable
    {
        private readonly bool _ownsContent;

        public UploadContentLease(Stream content, long length, long startPosition, bool ownsContent)
        {
            Content = content;
            Length = length;
            StartPosition = startPosition;
            _ownsContent = ownsContent;
        }

        public Stream Content { get; }

        public long Length { get; }

        public long StartPosition { get; }

        public void Dispose()
        {
            if (_ownsContent)
            {
                Content.Dispose();
            }
        }
    }
}
