using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.GenericS3;

namespace AtomBox.Providers.Tests;

public sealed class GenericS3IntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenericS3Integration_RealMinioFunctionalAndMultipart_LeavesArtifacts()
    {
        var environment = S3CompatibleIntegrationEnvironment.TryRead(
            StorageProviderRegistry.S3CompatibleProviderId,
            "Generic S3",
            "GENERIC_S3");
        if (environment is null)
        {
            return;
        }

        await EnsureBucketExistsAsync(environment);
        await RunBucketListTestAsync(environment);
        await S3CompatibleIntegrationTestHarness.RunVisibleFunctionalTestAsync(
            environment,
            (descriptor, resolver) => new GenericS3ProviderCreator(descriptor, resolver));
        await S3CompatibleIntegrationTestHarness.RunBucketRootPageTestAsync(
            environment,
            (descriptor, resolver) => new GenericS3ProviderCreator(descriptor, resolver));
        await RunMultipartTestAsync(environment);
        await RunDeleteTestAsync(environment);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenericS3Integration_InvalidSecret_IsMappedToAuthentication()
    {
        var environment = S3CompatibleIntegrationEnvironment.TryRead(
            StorageProviderRegistry.S3CompatibleProviderId,
            "Generic S3",
            "GENERIC_S3");
        if (environment is null)
        {
            return;
        }

        var invalidEnvironment = environment with { AccessKeySecret = environment.AccessKeySecret + "-invalid" };
        await using var provider = await S3CompatibleIntegrationTestHarness.CreateProviderAsync(
            invalidEnvironment,
            (descriptor, resolver) => new GenericS3ProviderCreator(descriptor, resolver));
        var result = await provider.ListAsync(new RemotePath(environment.Bucket, RemotePathKind.BucketRoot));

        Assert.True(result.IsFailure);
        Assert.Equal(AtomBox.Core.Errors.StorageErrorCategory.Authentication, result.Error?.Category);
    }

    private static async Task EnsureBucketExistsAsync(S3CompatibleIntegrationEnvironment environment)
    {
        AWSCredentials credentials = string.IsNullOrWhiteSpace(environment.SessionToken)
            ? new BasicAWSCredentials(environment.AccessKeyId, environment.AccessKeySecret)
            : new SessionAWSCredentials(environment.AccessKeyId, environment.AccessKeySecret, environment.SessionToken);
        using var client = new AmazonS3Client(credentials, new AmazonS3Config
        {
            ServiceURL = environment.Endpoint,
            AuthenticationRegion = environment.Region,
            ForcePathStyle = true
        });

        try
        {
            await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = environment.Bucket,
                MaxKeys = 1
            });
        }
        catch (AmazonS3Exception exception) when (
            exception.StatusCode == System.Net.HttpStatusCode.NotFound ||
            string.Equals(exception.ErrorCode, "NoSuchBucket", StringComparison.OrdinalIgnoreCase))
        {
            await client.PutBucketAsync(new PutBucketRequest { BucketName = environment.Bucket });
        }
    }

    private static async Task RunBucketListTestAsync(S3CompatibleIntegrationEnvironment environment)
    {
        await using var provider = await S3CompatibleIntegrationTestHarness.CreateProviderAsync(
            environment,
            (descriptor, resolver) => new GenericS3ProviderCreator(descriptor, resolver));

        var result = await provider.ListAsync(RemotePath.Root);

        Assert.True(result.IsSuccess, S3CompatibleIntegrationTestHarness.FormatError(
            "Generic S3 root Bucket list failed.", result.Error));
        Assert.Contains(result.GetValueOrThrow(), item =>
            item.Kind == RemoteItemKind.Bucket && item.Name == environment.Bucket);
    }

    private static async Task RunMultipartTestAsync(S3CompatibleIntegrationEnvironment environment)
    {
        await using var provider = await S3CompatibleIntegrationTestHarness.CreateProviderAsync(
            environment,
            (descriptor, resolver) => new GenericS3ProviderCreator(descriptor, resolver));

        var payload = new byte[6 * 1024 * 1024];
        new Random(101).NextBytes(payload);
        var key = $"{environment.CreateVisibleFolderPrefix()}multipart-6mb.bin";
        var path = new RemotePath($"{environment.Bucket}/{key}", RemotePathKind.ObjectPath);
        await using var source = new MemoryStream(payload, writable: false);
        var upload = await provider.UploadAsync(path, source, source.Length);
        Assert.True(upload.IsSuccess, S3CompatibleIntegrationTestHarness.FormatError("Generic S3 multipart upload failed.", upload.Error));

        await using var destination = new MemoryStream();
        var download = await provider.DownloadAsync(path, destination);
        Assert.True(download.IsSuccess, S3CompatibleIntegrationTestHarness.FormatError("Generic S3 multipart download failed.", download.Error));
        Assert.Equal(payload, destination.ToArray());
    }

    private static async Task RunDeleteTestAsync(S3CompatibleIntegrationEnvironment environment)
    {
        await using var provider = await S3CompatibleIntegrationTestHarness.CreateProviderAsync(
            environment,
            (descriptor, resolver) => new GenericS3ProviderCreator(descriptor, resolver));
        var folderPrefix = environment.CreateVisibleFolderPrefix();
        var folder = new RemotePath($"{environment.Bucket}/{folderPrefix.TrimEnd('/')}", RemotePathKind.Folder);
        var path = new RemotePath($"{environment.Bucket}/{folderPrefix}delete-verification.txt", RemotePathKind.ObjectPath);
        await using var source = new MemoryStream("delete-me"u8.ToArray(), writable: false);
        var upload = await provider.UploadAsync(path, source, source.Length);
        Assert.True(upload.IsSuccess, S3CompatibleIntegrationTestHarness.FormatError("Generic S3 delete setup upload failed.", upload.Error));

        var delete = await provider.DeleteAsync(path);
        Assert.True(delete.IsSuccess, S3CompatibleIntegrationTestHarness.FormatError("Generic S3 delete failed.", delete.Error));
        var list = await provider.ListAsync(folder);
        Assert.True(list.IsSuccess, S3CompatibleIntegrationTestHarness.FormatError("Generic S3 delete verification list failed.", list.Error));
        Assert.DoesNotContain(list.GetValueOrThrow(), item => item.Name == "delete-verification.txt");
    }
}
