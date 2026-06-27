using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace AtomBox.Providers.FileTransfer.WebDav;

internal sealed class WebDavHttpClientAdapter : IWebDavClientAdapter
{
    private static readonly XNamespace Dav = "DAV:";
    private readonly HttpClient _client;
    private readonly Uri _baseUri;

    public WebDavHttpClientAdapter(
        Uri baseUri,
        string? username,
        string? password,
        int timeoutSeconds)
    {
        _baseUri = NormalizeBaseUri(baseUri);
        _client = new HttpClient(new HttpClientHandler
        {
            UseProxy = false
        })
        {
            BaseAddress = _baseUri,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

        if (!string.IsNullOrWhiteSpace(username))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password ?? string.Empty}"));
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    public IReadOnlyList<WebDavRemoteEntry> ListDirectory(string path)
    {
        var entries = PropFind(path, "1");
        return entries
            .Where(entry => !IsSamePath(entry.Path, path))
            .Select(entry => entry.ToRemoteEntry())
            .ToArray();
    }

    public bool FileExists(string path)
    {
        return TryGetEntry(path, out var entry) && !entry.IsDirectory;
    }

    public bool DirectoryExists(string path)
    {
        return TryGetEntry(path, out var entry) && entry.IsDirectory;
    }

    public void CreateDirectory(string path)
    {
        using var response = Send(new HttpRequestMessage(new HttpMethod("MKCOL"), BuildUri(path)));
        if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.MethodNotAllowed)
        {
            return;
        }

        EnsureSuccess(response);
    }

    public void Delete(string path)
    {
        using var response = Send(new HttpRequestMessage(HttpMethod.Delete, BuildUri(path)));
        EnsureSuccess(response, HttpStatusCode.NoContent, HttpStatusCode.OK, HttpStatusCode.MultiStatus);
    }

    public void Move(string sourcePath, string destinationPath)
    {
        using var request = new HttpRequestMessage(new HttpMethod("MOVE"), BuildUri(sourcePath));
        request.Headers.TryAddWithoutValidation("Destination", BuildUri(destinationPath).AbsoluteUri);
        request.Headers.TryAddWithoutValidation("Overwrite", "F");
        using var response = Send(request);
        EnsureSuccess(response, HttpStatusCode.Created, HttpStatusCode.NoContent);
    }

    public void UploadFile(Stream content, string path, Action<long>? progress = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUri(path));
        request.Content = new ProgressHttpContent(content, progress);
        using var response = Send(request);
        EnsureSuccess(response, HttpStatusCode.Created, HttpStatusCode.NoContent, HttpStatusCode.OK);
    }

    public void DownloadFile(string path, Stream destination, Action<long>? progress = null)
    {
        using var response = Send(new HttpRequestMessage(HttpMethod.Get, BuildUri(path)));
        EnsureSuccess(response, HttpStatusCode.OK);
        using var stream = response.Content.ReadAsStream();
        var buffer = new byte[81920];
        long transferred = 0;
        while (true)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            destination.Write(buffer, 0, read);
            transferred += read;
            progress?.Invoke(transferred);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private bool TryGetEntry(string path, out WebDavEntry entry)
    {
        try
        {
            entry = PropFind(path, "0").First(item => IsSamePath(item.Path, path));
            return true;
        }
        catch (WebDavHttpException exception) when (exception.StatusCode == 404)
        {
            entry = default!;
            return false;
        }
        catch (InvalidOperationException)
        {
            entry = default!;
            return false;
        }
    }

    private IReadOnlyList<WebDavEntry> PropFind(string path, string depth)
    {
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), BuildUri(path));
        request.Headers.TryAddWithoutValidation("Depth", depth);
        request.Content = new StringContent(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <d:propfind xmlns:d="DAV:">
              <d:prop>
                <d:displayname />
                <d:resourcetype />
                <d:getcontentlength />
                <d:getlastmodified />
              </d:prop>
            </d:propfind>
            """,
            Encoding.UTF8,
            "application/xml");

        using var response = Send(request);
        EnsureSuccess(response, HttpStatusCode.MultiStatus);
        var xml = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var document = XDocument.Parse(xml);
        return document
            .Descendants(Dav + "response")
            .Select(ParseEntry)
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .ToArray();
    }

    private WebDavEntry? ParseEntry(XElement response)
    {
        var href = response.Element(Dav + "href")?.Value;
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var prop = response
            .Elements(Dav + "propstat")
            .Select(item => item.Element(Dav + "prop"))
            .FirstOrDefault(item => item is not null);
        if (prop is null)
        {
            return null;
        }

        var decodedPath = Uri.UnescapeDataString(new Uri(_baseUri, href).AbsolutePath);
        var isDirectory =
            prop.Element(Dav + "resourcetype")?.Element(Dav + "collection") is not null ||
            decodedPath.EndsWith("/", StringComparison.Ordinal);
        var displayName = prop.Element(Dav + "displayname")?.Value;
        var name = string.IsNullOrWhiteSpace(displayName)
            ? GetName(decodedPath)
            : displayName.Trim();
        long? length = long.TryParse(
            prop.Element(Dav + "getcontentlength")?.Value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsedLength)
            ? parsedLength
            : null;
        DateTimeOffset? modified = DateTimeOffset.TryParse(
            prop.Element(Dav + "getlastmodified")?.Value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsedModified)
            ? parsedModified
            : null;

        return new WebDavEntry(NormalizePath(decodedPath), name, isDirectory, length, modified);
    }

    private HttpResponseMessage Send(HttpRequestMessage request)
    {
        return _client.Send(request, HttpCompletionOption.ResponseHeadersRead);
    }

    private static void EnsureSuccess(HttpResponseMessage response, params HttpStatusCode[] accepted)
    {
        if (accepted.Contains(response.StatusCode))
        {
            return;
        }

        throw new WebDavHttpException((int)response.StatusCode, response.ReasonPhrase ?? string.Empty);
    }

    private Uri BuildUri(string path)
    {
        var relativePath = path.TrimStart('/');
        return new Uri(_baseUri, relativePath);
    }

    private bool IsSamePath(string left, string right)
    {
        return string.Equals(
            NormalizeComparablePath(left),
            NormalizeComparablePath(new Uri(_baseUri, right.TrimStart('/')).AbsolutePath),
            StringComparison.Ordinal);
    }

    private static Uri NormalizeBaseUri(Uri baseUri)
    {
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("WebDAV endpoint must be an absolute http or https URL.", nameof(baseUri));
        }

        var value = baseUri.ToString();
        return value.EndsWith("/", StringComparison.Ordinal)
            ? baseUri
            : new Uri(value + "/");
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized.TrimEnd('/');
    }

    internal static string NormalizeComparablePath(string path)
    {
        return NormalizePath(Uri.UnescapeDataString(path));
    }

    private static string GetName(string path)
    {
        var trimmed = path.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        return index < 0 ? trimmed : trimmed[(index + 1)..];
    }

    private sealed record WebDavEntry(
        string Path,
        string Name,
        bool IsDirectory,
        long? Length,
        DateTimeOffset? LastModified)
    {
        public WebDavRemoteEntry ToRemoteEntry()
        {
            return new WebDavRemoteEntry(Name, IsDirectory, Length, LastModified);
        }
    }

    private sealed class ProgressHttpContent : HttpContent
    {
        private readonly Stream _source;
        private readonly Action<long>? _progress;

        public ProgressHttpContent(Stream source, Action<long>? progress)
        {
            _source = source;
            _progress = progress;
        }

        protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            var buffer = new byte[81920];
            long transferred = 0;
            while (true)
            {
                var read = _source.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                stream.Write(buffer, 0, read);
                transferred += read;
                _progress?.Invoke(transferred);
            }
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buffer = new byte[81920];
            long transferred = 0;
            while (true)
            {
                var read = await _source.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await stream.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                transferred += read;
                _progress?.Invoke(transferred);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            if (_source.CanSeek)
            {
                length = _source.Length - _source.Position;
                return true;
            }

            length = 0;
            return false;
        }
    }
}
