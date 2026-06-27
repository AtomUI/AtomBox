using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AtomBox.Providers.ObjectStorage.Upyun;

internal sealed class UpyunRestClient : IUpyunClient
{
    private const long MultipartPartSize = 1024L * 1024L;

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _bucketName;

    public UpyunRestClient(string endpoint, string bucketName, string operatorName, string password, HttpClient? httpClient = null)
    {
        _endpoint = NormalizeEndpoint(endpoint);
        _bucketName = bucketName.Trim();
        _httpClient = httpClient ?? new HttpClient();
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{operatorName}:{password}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    public IReadOnlyList<UpyunBucket> ListBuckets()
    {
        return [new UpyunBucket(_bucketName)];
    }

    public UpyunObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000)
    {
        _ = cursor;
        _ = maxKeys;
        var normalizedPrefix = NormalizeKey(prefix);
        var uri = BuildUri(bucketName, normalizedPrefix);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = _httpClient.Send(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new UpyunObjectListing([], []);
        }

        EnsureSuccess(response, "list");

        var payload = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new UpyunObjectListing([], []);
        }

        return ParseListing(normalizedPrefix, payload);
    }

    public void DeleteObject(string bucketName, string key)
    {
        using var response = _httpClient.Send(new HttpRequestMessage(HttpMethod.Delete, BuildUri(bucketName, NormalizeKey(key))));
        EnsureSuccess(response, "delete");
    }

    public void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUri(destinationBucketName, NormalizeKey(destinationKey)));
        request.Headers.TryAddWithoutValidation("X-Upyun-Copy-Source", $"/{sourceBucketName.Trim('/')}/{NormalizeKey(sourceKey)}");
        using var response = _httpClient.Send(request);
        EnsureSuccess(response, "copy");
    }

    public void PutObject(string bucketName, string key, Stream content)
    {
        var normalizedKey = NormalizeKey(key);
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUri(bucketName, NormalizeKey(key)))
        {
            Content = new StreamContent(content)
        };
        if (normalizedKey.EndsWith("/", StringComparison.Ordinal))
        {
            request.Headers.TryAddWithoutValidation("Folder", "true");
        }

        using var response = _httpClient.Send(request);
        EnsureSuccess(response, normalizedKey.EndsWith("/", StringComparison.Ordinal) ? "create-folder" : "put-object");
    }

    public void PutObjectMultipart(string bucketName, string key, Stream content, long contentLength, Action<long, long>? progress = null)
    {
        var normalizedKey = NormalizeKey(key);
        var uploadId = InitiateMultipartUpload(bucketName, normalizedKey, contentLength);
        var transferred = 0L;
        try
        {
            var partNumber = 0;
            foreach (var part in ReadParts(content, MultipartPartSize))
            {
                UploadMultipartPart(bucketName, normalizedKey, uploadId, partNumber, part);
                transferred += part.LongLength;
                progress?.Invoke(Math.Min(contentLength, transferred), contentLength);
                partNumber++;
            }

            CompleteMultipartUpload(bucketName, normalizedKey, uploadId);
            progress?.Invoke(contentLength, contentLength);
        }
        catch
        {
            TryAbortMultipartUpload(bucketName, normalizedKey, uploadId);
            throw;
        }
    }

    public UpyunDownloadObject GetObject(string bucketName, string key)
    {
        using var response = _httpClient.Send(new HttpRequestMessage(HttpMethod.Get, BuildUri(bucketName, NormalizeKey(key))));
        EnsureSuccess(response, "get-object");
        var payload = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        return new UpyunDownloadObject(new MemoryStream(payload, writable: false), payload.LongLength);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static UpyunObjectListing ParseListing(string prefix, string payload)
    {
        var objects = new List<UpyunObjectSummary>();
        var folders = new SortedSet<string>(StringComparer.Ordinal);

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                ParseJsonItems(document.RootElement.EnumerateArray(), prefix, objects, folders);
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object &&
                     document.RootElement.TryGetProperty("files", out var files) &&
                     files.ValueKind == JsonValueKind.Array)
            {
                ParseJsonItems(files.EnumerateArray(), prefix, objects, folders);
            }
        }
        catch (JsonException)
        {
            return ParseTextListing(prefix, payload);
        }

        return new UpyunObjectListing(objects, folders.ToArray());
    }

    private static void ParseJsonItems(
        JsonElement.ArrayEnumerator items,
        string prefix,
        List<UpyunObjectSummary> objects,
        SortedSet<string> folders)
    {
        foreach (var item in items)
        {
            var name = TryGetString(item, "name") ?? TryGetString(item, "file_name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var type = TryGetString(item, "type");
            var key = string.IsNullOrWhiteSpace(prefix) ? name : $"{prefix}{name}";
            if (string.Equals(type, "folder", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "dir", StringComparison.OrdinalIgnoreCase))
            {
                folders.Add(key.TrimEnd('/') + "/");
            }
            else
            {
                objects.Add(new UpyunObjectSummary(
                    key,
                    TryGetInt64(item, "size") ?? TryGetInt64(item, "length") ?? 0,
                    TryGetUnixTime(item, "time") ?? TryGetUnixTime(item, "last_modified"),
                    TryGetString(item, "etag"),
                    type));
            }
        }
    }

    private static UpyunObjectListing ParseTextListing(string prefix, string payload)
    {
        var objects = new List<UpyunObjectSummary>();
        var folders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var line in payload.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            var name = parts[0];
            var key = string.IsNullOrWhiteSpace(prefix) ? name : $"{prefix}{name}";
            if (parts.Length > 1 && string.Equals(parts[1], "F", StringComparison.OrdinalIgnoreCase))
            {
                folders.Add(key.TrimEnd('/') + "/");
                continue;
            }

            var size = parts.Length > 2 && long.TryParse(parts[2], out var parsedSize) ? parsedSize : 0;
            objects.Add(new UpyunObjectSummary(key, size, null, null, null));
        }

        return new UpyunObjectListing(objects, folders.ToArray());
    }

    private static string? TryGetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? TryGetInt64(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.TryGetInt64(out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? TryGetUnixTime(JsonElement element, string property)
    {
        var unix = TryGetInt64(element, property);
        return unix is > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix.Value) : null;
    }

    private Uri BuildUri(string bucketName, string key, string? query = null)
    {
        var path = string.IsNullOrWhiteSpace(key)
            ? $"/{bucketName.Trim('/')}/"
            : $"/{bucketName.Trim('/')}/{key.TrimStart('/')}";
        var builder = new UriBuilder(_endpoint)
        {
            Path = path,
            Query = query ?? string.Empty
        };
        return builder.Uri;
    }

    private string InitiateMultipartUpload(string bucketName, string key, long contentLength)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUri(bucketName, key));
        request.Headers.TryAddWithoutValidation("X-Upyun-Multi-Stage", "initiate");
        request.Headers.TryAddWithoutValidation("X-Upyun-Multi-Length", contentLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-Upyun-Multi-Type", "application/octet-stream");
        using var response = _httpClient.Send(request);
        EnsureSuccess(response, "multipart-initiate");
        return TryGetHeader(response, "X-Upyun-Multi-UUID") ??
               TryGetHeader(response, "X-Upyun-Multi-Uuid") ??
               throw new InvalidOperationException("Upyun multipart upload id was empty.");
    }

    private void UploadMultipartPart(string bucketName, string key, string uploadId, int partNumber, byte[] payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUri(bucketName, key))
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentLength = payload.LongLength;
        request.Headers.TryAddWithoutValidation("X-Upyun-Multi-Stage", "upload");
        request.Headers.TryAddWithoutValidation("X-Upyun-Multi-UUID", uploadId);
        request.Headers.TryAddWithoutValidation("X-Upyun-Part-ID", partNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("Content-MD5", Convert.ToBase64String(MD5.HashData(payload)));
        using var response = _httpClient.Send(request);
        EnsureSuccess(response, $"multipart-upload-part-{partNumber}");
    }

    private void CompleteMultipartUpload(string bucketName, string key, string uploadId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUri(bucketName, key));
        request.Headers.TryAddWithoutValidation("X-Upyun-Multi-Stage", "complete");
        request.Headers.TryAddWithoutValidation("X-Upyun-Multi-UUID", uploadId);
        using var response = _httpClient.Send(request);
        EnsureSuccess(response, "multipart-complete");
    }

    private void TryAbortMultipartUpload(string bucketName, string key, string uploadId)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, BuildUri(bucketName, key));
            request.Headers.TryAddWithoutValidation("X-Upyun-Multi-Stage", "abort");
            request.Headers.TryAddWithoutValidation("X-Upyun-Multi-UUID", uploadId);
            using var response = _httpClient.Send(request);
            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch
        {
        }
    }

    private static string? TryGetHeader(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault()
            : null;
    }

    private static IEnumerable<byte[]> ReadParts(Stream content, long partSize)
    {
        var buffer = new byte[checked((int)partSize)];
        while (true)
        {
            var readTotal = 0;
            while (readTotal < buffer.Length)
            {
                var read = content.Read(buffer, readTotal, buffer.Length - readTotal);
                if (read == 0)
                {
                    break;
                }

                readTotal += read;
            }

            if (readTotal == 0)
            {
                yield break;
            }

            if (readTotal == buffer.Length)
            {
                yield return buffer;
                buffer = new byte[checked((int)partSize)];
            }
            else
            {
                var part = new byte[readTotal];
                Array.Copy(buffer, part, readTotal);
                yield return part;
                yield break;
            }
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = string.Empty;
        try
        {
            body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(body)
                ? $"Upyun {operation} request failed with status {(int)response.StatusCode}."
                : $"Upyun {operation} request failed with status {(int)response.StatusCode}: {body}",
            null,
            response.StatusCode);
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }

    private static string NormalizeKey(string key)
    {
        return key.Trim().TrimStart('/');
    }
}
