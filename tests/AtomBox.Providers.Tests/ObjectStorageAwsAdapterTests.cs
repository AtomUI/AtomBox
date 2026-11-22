using System.Net.Http.Headers;
using Amazon.Runtime;
using AtomBox.Core.Errors;
using AtomBox.Providers.ObjectStorage.AliyunOss;
using AtomBox.Providers.ObjectStorage.BaiduBos;
using AtomBox.Providers.ObjectStorage.HuaweiObs;
using AtomBox.Providers.ObjectStorage.QiniuKodo;
using AtomBox.Providers.ObjectStorage.S3Compatible;
using AtomBox.Providers.ObjectStorage.TencentCos;
using AtomBox.Providers.ObjectStorage.VolcengineTos;

namespace AtomBox.Providers.Tests;

public sealed class ObjectStorageAwsAdapterTests
{
    [Theory]
    [InlineData("https://example-1250000000.cos.ap-chengdu.myqcloud.com", "https://cos.ap-chengdu.myqcloud.com")]
    [InlineData("cos.ap-chengdu.myqcloud.com", "https://cos.ap-chengdu.myqcloud.com")]
    [InlineData("https://storage.example.com", "https://storage.example.com")]
    public void TencentCosProfile_UsesRegionalEndpoint(string input, string expected)
    {
        Assert.Equal(expected, TencentCosProviderCreator.NormalizeServiceEndpoint(input).GetValueOrThrow());
    }

    [Theory]
    [InlineData("archive-v2-1250000000", true)]
    [InlineData("archive-v2", false)]
    [InlineData("Archive-1250000000", false)]
    [InlineData("archive--1250000000", false)]
    [InlineData("-1250000000", false)]
    [InlineData("archive-", false)]
    public void TencentCosProfile_ValidatesFullBucketName(string name, bool valid)
    {
        Assert.Equal(valid, TencentCosProviderCreator.ValidateBucketName(name) is null);
    }

    [Theory]
    [InlineData(
        "oss-cn-hangzhou.aliyuncs.com",
        null,
        "cn-hangzhou",
        "https://s3.oss-cn-hangzhou.aliyuncs.com",
        "https://oss-cn-hangzhou.aliyuncs.com",
        false)]
    [InlineData(
        "https://s3.oss-cn-beijing-internal.aliyuncs.com",
        "cn-beijing",
        "cn-beijing",
        "https://s3.oss-cn-beijing-internal.aliyuncs.com",
        "https://oss-cn-beijing-internal.aliyuncs.com",
        true)]
    public void AliyunEndpointProfile_ProducesFixedS3AndNativeRoutes(
        string endpoint,
        string? configuredRegion,
        string expectedRegion,
        string expectedS3Endpoint,
        string expectedNativeEndpoint,
        bool expectedInternal)
    {
        var result = AliyunOssEndpointProfile.TryCreate(endpoint, configuredRegion);

        var profile = result.GetValueOrThrow();
        Assert.Equal(expectedRegion, profile.Region);
        Assert.Equal(expectedS3Endpoint, profile.S3Endpoint);
        Assert.Equal(expectedNativeEndpoint, profile.NativeEndpoint);
        Assert.Equal(expectedInternal, profile.IsInternal);
    }

    [Theory]
    [InlineData("https://custom.example.com", null)]
    [InlineData("https://oss-cn-hangzhou.aliyuncs.com/path", null)]
    [InlineData("https://oss-cn-hangzhou.aliyuncs.com", "cn-beijing")]
    public void AliyunEndpointProfile_RejectsUnsupportedOrConflictingConfiguration(
        string endpoint,
        string? configuredRegion)
    {
        var result = AliyunOssEndpointProfile.TryCreate(endpoint, configuredRegion);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public void OssV4Signer_UsesAliyunPublishedCanonicalRequestWithFixedTestSecret()
    {
        using var content = new ByteArrayContent("abc"u8.ToArray());
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment");
        content.Headers.ContentLength = 3;
        content.Headers.ContentMD5 = Convert.FromBase64String("ICy5YqxZB1uWSwcVLSNLcA==");
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "https://oss-cn-hangzhou.aliyuncs.com/examplebucket/exampleobject")
        {
            Content = content
        };

        OssV4Signer.Sign(
            request,
            "LTAIEXAMPLE",
            "yourAccessKeySecret",
            "cn-hangzhou",
            timestamp: new DateTimeOffset(2025, 4, 11, 6, 41, 24, TimeSpan.Zero),
            additionalHeaders: ["content-disposition", "content-length"]);

        var authorization = Assert.Single(request.Headers.GetValues("Authorization"));
        Assert.Equal(
            "OSS4-HMAC-SHA256 Credential=LTAIEXAMPLE/20250411/cn-hangzhou/oss/aliyun_v4_request," +
            "AdditionalHeaders=content-disposition;content-length," +
            "Signature=d3694c2dfc5371ee6acd35e88c4871ac95a7ba01d3a2f476768fe61218590097",
            authorization);
    }

    [Fact]
    public void OssV4Signer_IncludesSecurityTokenInCanonicalHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://oss-cn-hangzhou.aliyuncs.com/");

        OssV4Signer.Sign(
            request,
            "access",
            "secret",
            "cn-hangzhou",
            securityToken: "session-token",
            timestamp: new DateTimeOffset(2025, 4, 11, 6, 41, 24, TimeSpan.Zero));

        Assert.Equal("session-token", Assert.Single(request.Headers.GetValues("x-oss-security-token")));
        Assert.StartsWith("OSS4-HMAC-SHA256 Credential=access/", Assert.Single(request.Headers.GetValues("Authorization")));
    }

    [Fact]
    public async Task NativeListBuckets_ParsesBucketsAndUsesOssV4Authorization()
    {
        string? authorization = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            authorization = Assert.Single(request.Headers.GetValues("Authorization"));
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<ListAllMyBucketsResult><Buckets><Bucket><Name>bucket-a</Name>" +
                    "<CreationDate>2025-01-02T03:04:05Z</CreationDate></Bucket></Buckets></ListAllMyBucketsResult>")
            });
        }));
        using var client = new AliyunOssNativeServiceClient(
            "https://oss-cn-hangzhou.aliyuncs.com",
            "cn-hangzhou",
            "access",
            "secret",
            securityToken: null,
            httpClient);

        var buckets = await client.ListBucketsAsync(CancellationToken.None);

        Assert.Equal("bucket-a", Assert.Single(buckets).Name);
        Assert.StartsWith("OSS4-HMAC-SHA256", authorization);
    }

    [Fact]
    public async Task NativeListBuckets_PropagatesCancellationToHttpClient()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }));
        using var client = new AliyunOssNativeServiceClient(
            "https://oss-cn-hangzhou.aliyuncs.com",
            "cn-hangzhou",
            "access",
            "secret",
            securityToken: null,
            httpClient);
        using var cancellation = new CancellationTokenSource();

        var operation = client.ListBucketsAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public void AwsProfile_UsesVirtualHostAndCompatibilityChecksumPolicy()
    {
        var profile = new AwsSdkS3ProviderProfile(
            "https://s3.oss-cn-hangzhou.aliyuncs.com",
            "cn-hangzhou",
            ForcePathStyle: false,
            CursorNamespace: "aliyun-oss");

        var config = AwsSdkS3ProviderFactory.CreateAmazonConfig(profile);

        Assert.False(config.ForcePathStyle);
        Assert.Equal("cn-hangzhou", config.AuthenticationRegion);
        Assert.Equal(RequestChecksumCalculation.WHEN_REQUIRED, config.RequestChecksumCalculation);
        Assert.Equal(ResponseChecksumValidation.WHEN_REQUIRED, config.ResponseChecksumValidation);
    }

    [Theory]
    [InlineData("bj.bcebos.com", "https://s3.bj.bcebos.com")]
    [InlineData("https://s3.gz.bcebos.com", "https://s3.gz.bcebos.com")]
    public void BaiduProfile_MapsNativeEndpointToS3Endpoint(string input, string expected)
    {
        Assert.Equal(expected, BaiduBosProviderCreator.TryCreateS3Endpoint(input).GetValueOrThrow());
    }

    [Theory]
    [InlineData("tos-cn-beijing.volces.com", "https://tos-s3-cn-beijing.volces.com")]
    [InlineData("https://tos-s3-cn-shanghai.ivolces.com", "https://tos-s3-cn-shanghai.ivolces.com")]
    public void VolcengineProfile_MapsNativeEndpointToS3Endpoint(string input, string expected)
    {
        Assert.Equal(expected, VolcengineTosProviderCreator.TryCreateS3Endpoint(input).GetValueOrThrow());
    }

    [Theory]
    [InlineData("z0", "cn-east-1")]
    [InlineData("as0", "ap-southeast-1")]
    [InlineData("cn-east-2", "cn-east-2")]
    public void QiniuProfile_NormalizesLegacyRegionNames(string input, string expected)
    {
        Assert.Equal(expected, QiniuKodoProviderCreator.NormalizeRegion(input));
    }

    [Fact]
    public void HuaweiProfile_DerivesRegionFromStandardEndpoint()
    {
        Assert.Equal(
            "cn-north-4",
            HuaweiObsProviderCreator.TryDeriveRegion("https://obs.cn-north-4.myhuaweicloud.com"));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
