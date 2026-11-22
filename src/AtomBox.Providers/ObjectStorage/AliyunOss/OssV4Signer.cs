using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AtomBox.Providers.ObjectStorage.AliyunOss;

internal static class OssV4Signer
{
    private const string Algorithm = "OSS4-HMAC-SHA256";
    private const string UnsignedPayload = "UNSIGNED-PAYLOAD";

    public static void Sign(
        HttpRequestMessage request,
        string accessKeyId,
        string accessKeySecret,
        string region,
        string? securityToken = null,
        DateTimeOffset? timestamp = null,
        IEnumerable<string>? additionalHeaders = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = (timestamp ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var dateTime = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        request.Headers.Remove("x-oss-date");
        request.Headers.TryAddWithoutValidation("x-oss-date", dateTime);
        request.Headers.Remove("x-oss-content-sha256");
        request.Headers.TryAddWithoutValidation("x-oss-content-sha256", UnsignedPayload);
        if (!string.IsNullOrWhiteSpace(securityToken))
        {
            request.Headers.Remove("x-oss-security-token");
            request.Headers.TryAddWithoutValidation("x-oss-security-token", securityToken);
        }

        var canonicalHeaders = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["x-oss-content-sha256"] = UnsignedPayload,
            ["x-oss-date"] = dateTime
        };
        if (!string.IsNullOrWhiteSpace(securityToken))
        {
            canonicalHeaders["x-oss-security-token"] = securityToken.Trim();
        }

        AddHeaderIfPresent(request, canonicalHeaders, "content-md5");
        AddHeaderIfPresent(request, canonicalHeaders, "content-type");
        var normalizedAdditionalHeaders = (additionalHeaders ?? [])
            .Select(item => item.Trim().ToLowerInvariant())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        foreach (var headerName in normalizedAdditionalHeaders)
        {
            if (!AddHeaderIfPresent(request, canonicalHeaders, headerName))
            {
                throw new ArgumentException($"Additional signed header '{headerName}' does not exist on the request.", nameof(additionalHeaders));
            }
        }

        var canonicalHeaderText = string.Join(
            "\n",
            canonicalHeaders.Select(item => $"{item.Key}:{NormalizeHeaderValue(item.Value)}"));
        var canonicalRequest = string.Join(
            "\n",
            request.Method.Method.ToUpperInvariant(),
            CanonicalizePath(request.RequestUri?.AbsolutePath),
            CanonicalizeQuery(request.RequestUri?.Query),
            canonicalHeaderText + "\n",
            string.Join(';', normalizedAdditionalHeaders),
            UnsignedPayload);
        var scope = $"{date}/{region}/oss/aliyun_v4_request";
        var stringToSign = $"{Algorithm}\n{dateTime}\n{scope}\n{Sha256Hex(canonicalRequest)}";

        var dateKey = HmacSha256(Encoding.UTF8.GetBytes("aliyun_v4" + accessKeySecret), date);
        var regionKey = HmacSha256(dateKey, region);
        var serviceKey = HmacSha256(regionKey, "oss");
        var signingKey = HmacSha256(serviceKey, "aliyun_v4_request");
        var signature = Convert.ToHexString(HmacSha256(signingKey, stringToSign)).ToLowerInvariant();

        request.Headers.Remove("Authorization");
        var additionalAuthorization = normalizedAdditionalHeaders.Length == 0
            ? string.Empty
            : $",AdditionalHeaders={string.Join(';', normalizedAdditionalHeaders)}";
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"{Algorithm} Credential={accessKeyId}/{scope}{additionalAuthorization},Signature={signature}");
    }

    private static bool AddHeaderIfPresent(
        HttpRequestMessage request,
        IDictionary<string, string> destination,
        string headerName)
    {
        if (request.Headers.TryGetValues(headerName, out var requestValues) ||
            request.Content?.Headers.TryGetValues(headerName, out requestValues) == true)
        {
            destination[headerName] = string.Join(',', requestValues.Select(NormalizeHeaderValue));
            return true;
        }

        return false;
    }

    private static string CanonicalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        return string.Join('/', path.Split('/').Select(Uri.EscapeDataString)).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string CanonicalizeQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        return string.Join(
            "&",
            query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Split('=', 2))
                .Select(parts => new KeyValuePair<string, string?>(
                    Uri.EscapeDataString(Uri.UnescapeDataString(parts[0])),
                    parts.Length == 2 ? Uri.EscapeDataString(Uri.UnescapeDataString(parts[1])) : null))
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ThenBy(item => item.Value, StringComparer.Ordinal)
                .Select(item => item.Value is null ? item.Key : $"{item.Key}={item.Value}"));
    }

    private static string NormalizeHeaderValue(string value)
    {
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Sha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static byte[] HmacSha256(byte[] key, string value)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
    }
}
