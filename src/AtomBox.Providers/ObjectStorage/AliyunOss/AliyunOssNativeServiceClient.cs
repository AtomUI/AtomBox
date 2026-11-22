using System.Xml.Linq;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.AliyunOss;

internal sealed class AliyunOssNativeServiceClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Uri _serviceRoot;
    private readonly string _region;
    private readonly string _accessKeyId;
    private readonly string _accessKeySecret;
    private readonly string? _securityToken;

    public AliyunOssNativeServiceClient(
        string endpoint,
        string region,
        string accessKeyId,
        string accessKeySecret,
        string? securityToken,
        HttpClient? httpClient = null)
    {
        _serviceRoot = new Uri(endpoint.TrimEnd('/') + "/", UriKind.Absolute);
        _region = region;
        _accessKeyId = accessKeyId;
        _accessKeySecret = accessKeySecret;
        _securityToken = securityToken;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public async Task<IReadOnlyList<S3CompatibleBucket>> ListBucketsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _serviceRoot);
        OssV4Signer.Sign(request, _accessKeyId, _accessKeySecret, _region, _securityToken);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        }

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var document = await XDocument.LoadAsync(content, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "Bucket")
            .Select(element => new S3CompatibleBucket(
                element.Elements().FirstOrDefault(child => child.Name.LocalName == "Name")?.Value ?? string.Empty,
                ParseDate(element.Elements().FirstOrDefault(child => child.Name.LocalName == "CreationDate")?.Value)))
            .Where(bucket => !string.IsNullOrWhiteSpace(bucket.Name))
            .ToArray();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static async Task<AliyunOssNativeException> CreateExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var code = TryReadXmlValue(body, "Code");
        var requestId = TryReadXmlValue(body, "RequestId");
        return new AliyunOssNativeException((int)response.StatusCode, code, requestId);
    }

    private static string? TryReadXmlValue(string xml, string localName)
    {
        try
        {
            return XDocument.Parse(xml).Descendants().FirstOrDefault(item => item.Name.LocalName == localName)?.Value;
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var result) ? result : null;
    }
}
