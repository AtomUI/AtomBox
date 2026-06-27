using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtomBox.Providers.NetDisk.AliyunDrive;

internal sealed class AliyunDriveHttpClient : IAliyunDriveClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public AliyunDriveHttpClient(string endpoint, string accessToken, HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress = new Uri(NormalizeEndpoint(endpoint));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<IReadOnlyList<AliyunDriveFileItem>> ListAsync(
        string driveId,
        string parentFileId,
        CancellationToken cancellationToken = default)
    {
        var items = new List<AliyunDriveFileItem>();
        string? marker = null;
        do
        {
            var response = await PostAsync<AliyunDriveListResponse>(
                "/adrive/v1.0/openFile/list",
                new AliyunDriveListRequest(driveId, parentFileId, marker, 100),
                cancellationToken).ConfigureAwait(false);
            if (response.Items is not null)
            {
                items.AddRange(response.Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.FileId) && !string.IsNullOrWhiteSpace(item.Name))
                    .Select(item => new AliyunDriveFileItem(
                        item.FileId!,
                        item.Name!,
                        string.Equals(item.Type, "folder", StringComparison.OrdinalIgnoreCase),
                        item.Size,
                        TryParseDate(item.UpdatedAt),
                        item.ContentType)));
            }

            marker = string.IsNullOrWhiteSpace(response.NextMarker) ? null : response.NextMarker;
        }
        while (marker is not null);

        return items;
    }

    public async Task DeleteAsync(
        string driveId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        await PostAsync<JsonElement>(
            "/adrive/v1.0/openFile/delete",
            new AliyunDriveFileRequest(driveId, fileId),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task UploadAsync(
        string driveId,
        string parentFileId,
        string name,
        Stream content,
        long? contentLength,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var create = await PostAsync<AliyunDriveCreateResponse>(
            "/adrive/v1.0/openFile/create",
            new AliyunDriveCreateRequest(
                driveId,
                parentFileId,
                name,
                "file",
                "auto_rename",
                [new AliyunDrivePartInfo(1)]),
            cancellationToken).ConfigureAwait(false);
        var uploadUrl = create.PartInfoList?.FirstOrDefault()?.UploadUrl;
        if (string.IsNullOrWhiteSpace(create.FileId) || string.IsNullOrWhiteSpace(uploadUrl))
        {
            throw new AliyunDriveApiException(500, "MissingUploadUrl");
        }

        using var uploadContent = new ProgressStreamContent(content, contentLength, progress);
        using var uploadResponse = await _httpClient.PutAsync(uploadUrl, uploadContent, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(uploadResponse, cancellationToken).ConfigureAwait(false);

        await PostAsync<JsonElement>(
            "/adrive/v1.0/openFile/complete",
            new AliyunDriveCompleteRequest(driveId, create.FileId!, create.UploadId),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AliyunDriveDownloadObject> GetObjectAsync(
        string driveId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        var result = await PostAsync<AliyunDriveDownloadUrlResponse>(
            "/adrive/v1.0/openFile/getDownloadUrl",
            new AliyunDriveFileRequest(driveId, fileId),
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.Url))
        {
            throw new AliyunDriveApiException(500, "MissingDownloadUrl");
        }

        var response = await _httpClient.GetAsync(result.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new AliyunDriveDownloadObject(stream, response.Content.Headers.ContentLength, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return value ?? throw new AliyunDriveApiException(500, "EmptyResponse");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? code = null;
        try
        {
            var body = await response.Content.ReadFromJsonAsync<AliyunDriveErrorResponse>(
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            code = body?.Code;
        }
        catch (JsonException)
        {
        }

        throw new AliyunDriveApiException((int)response.StatusCode, code);
    }

    private static DateTimeOffset? TryParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }

    private sealed record AliyunDriveListRequest(
        [property: JsonPropertyName("drive_id")] string DriveId,
        [property: JsonPropertyName("parent_file_id")] string ParentFileId,
        [property: JsonPropertyName("marker")] string? Marker,
        [property: JsonPropertyName("limit")] int Limit);

    private sealed record AliyunDriveFileRequest(
        [property: JsonPropertyName("drive_id")] string DriveId,
        [property: JsonPropertyName("file_id")] string FileId);

    private sealed record AliyunDriveCreateRequest(
        [property: JsonPropertyName("drive_id")] string DriveId,
        [property: JsonPropertyName("parent_file_id")] string ParentFileId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("check_name_mode")] string CheckNameMode,
        [property: JsonPropertyName("part_info_list")] IReadOnlyList<AliyunDrivePartInfo> PartInfoList);

    private sealed record AliyunDrivePartInfo(
        [property: JsonPropertyName("part_number")] int PartNumber)
    {
        [JsonPropertyName("upload_url")]
        public string? UploadUrl { get; init; }
    }

    private sealed record AliyunDriveCompleteRequest(
        [property: JsonPropertyName("drive_id")] string DriveId,
        [property: JsonPropertyName("file_id")] string FileId,
        [property: JsonPropertyName("upload_id")] string? UploadId);

    private sealed record AliyunDriveListResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<AliyunDriveFileResponse>? Items,
        [property: JsonPropertyName("next_marker")] string? NextMarker);

    private sealed record AliyunDriveFileResponse(
        [property: JsonPropertyName("file_id")] string? FileId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("size")] long? Size,
        [property: JsonPropertyName("updated_at")] string? UpdatedAt,
        [property: JsonPropertyName("content_type")] string? ContentType);

    private sealed record AliyunDriveCreateResponse(
        [property: JsonPropertyName("file_id")] string? FileId,
        [property: JsonPropertyName("upload_id")] string? UploadId,
        [property: JsonPropertyName("part_info_list")] IReadOnlyList<AliyunDrivePartInfo>? PartInfoList);

    private sealed record AliyunDriveDownloadUrlResponse(
        [property: JsonPropertyName("url")] string? Url);

    private sealed record AliyunDriveErrorResponse(
        [property: JsonPropertyName("code")] string? Code);

    private sealed class ProgressStreamContent : HttpContent
    {
        private readonly Stream _source;
        private readonly long? _contentLength;
        private readonly IProgress<long>? _progress;

        public ProgressStreamContent(Stream source, long? contentLength, IProgress<long>? progress)
        {
            _source = source;
            _contentLength = contentLength;
            _progress = progress;
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
                _progress?.Report(transferred);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            if (_contentLength is { } value)
            {
                length = value;
                return true;
            }

            length = 0;
            return false;
        }
    }
}
