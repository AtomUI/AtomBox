using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Transfers;
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

    [Fact]
    [Trait("Category", "RealCancellationIntegration")]
    public async Task GenericS3CancellationIntegration_StopsPutMultipartAndDownload()
    {
        var environment = S3CompatibleIntegrationEnvironment.TryRead(
            StorageProviderRegistry.S3CompatibleProviderId,
            "Generic S3",
            "GENERIC_S3");
        if (environment is null || !CancellationIntegrationEnabled())
        {
            return;
        }

        await EnsureBucketExistsAsync(environment);
        await using var provider = await S3CompatibleIntegrationTestHarness.CreateProviderAsync(
            environment,
            (descriptor, resolver) => new GenericS3ProviderCreator(descriptor, resolver));
        using var verificationClient = CreateAmazonClient(environment);
        var runPrefix = $"{environment.CreateVisibleFolderPrefix()}cancel-{Guid.NewGuid():N}/";
        var putKey = $"{runPrefix}put.bin";
        var multipartKey = $"{runPrefix}multipart.bin";
        var downloadKey = $"{runPrefix}download.bin";

        try
        {
            await AssertUploadCanceledAsync(provider, environment.Bucket, putKey, 4 * 1024 * 1024);
            await AssertObjectMissingAsync(verificationClient, environment.Bucket, putKey);

            await AssertUploadCanceledAsync(provider, environment.Bucket, multipartKey, 6 * 1024 * 1024);
            await AssertObjectMissingAsync(verificationClient, environment.Bucket, multipartKey);
            var multipartUploads = await verificationClient.ListMultipartUploadsAsync(new ListMultipartUploadsRequest
            {
                BucketName = environment.Bucket,
                Prefix = multipartKey
            });
            Assert.DoesNotContain(multipartUploads.MultipartUploads ?? [], upload => upload.Key == multipartKey);

            var downloadPath = new RemotePath($"{environment.Bucket}/{downloadKey}", RemotePathKind.ObjectPath);
            await using (var setup = new MemoryStream(new byte[8 * 1024 * 1024], writable: false))
            {
                var setupResult = await provider.UploadAsync(downloadPath, setup, setup.Length);
                Assert.True(setupResult.IsSuccess, S3CompatibleIntegrationTestHarness.FormatError(
                    "Generic S3 cancellation download setup failed.", setupResult.Error));
            }

            using var downloadCancellation = new CancellationTokenSource();
            var downloadProgress = new CancelOnFirstProgress(downloadCancellation);
            await using var destination = new MemoryStream();
            var download = await provider.DownloadAsync(
                downloadPath,
                destination,
                downloadProgress,
                downloadCancellation.Token);
            Assert.Equal(StorageErrorCategory.Canceled, download.Error?.Category);
            Assert.True(downloadProgress.WasReported);
        }
        finally
        {
            foreach (var key in new[] { putKey, multipartKey, downloadKey })
            {
                await verificationClient.DeleteObjectAsync(environment.Bucket, key);
            }
        }
    }

    private static async Task EnsureBucketExistsAsync(S3CompatibleIntegrationEnvironment environment)
    {
        using var client = CreateAmazonClient(environment);

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

    private static AmazonS3Client CreateAmazonClient(S3CompatibleIntegrationEnvironment environment)
    {
        AWSCredentials credentials = string.IsNullOrWhiteSpace(environment.SessionToken)
            ? new BasicAWSCredentials(environment.AccessKeyId, environment.AccessKeySecret)
            : new SessionAWSCredentials(environment.AccessKeyId, environment.AccessKeySecret, environment.SessionToken);
        return new AmazonS3Client(credentials, new AmazonS3Config
        {
            ServiceURL = environment.Endpoint,
            AuthenticationRegion = environment.Region,
            ForcePathStyle = true
        });
    }

    private static async Task AssertUploadCanceledAsync(
        IStorageProvider provider,
        string bucketName,
        string key,
        int payloadSize)
    {
        var path = new RemotePath($"{bucketName}/{key}", RemotePathKind.ObjectPath);
        await using var source = new GatedReadStream(new byte[payloadSize]);
        using var cancellation = new CancellationTokenSource();

        var operation = Task.Run(() =>
            provider.UploadAsync(path, source, source.Length, cancellationToken: cancellation.Token));
        await source.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        source.Release();
        var result = await operation;

        Assert.Equal(StorageErrorCategory.Canceled, result.Error?.Category);
    }

    private static async Task AssertObjectMissingAsync(AmazonS3Client client, string bucketName, string key)
    {
        var result = await client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = bucketName,
            Prefix = key,
            MaxKeys = 2
        });
        Assert.DoesNotContain(result.S3Objects ?? [], item => item.Key == key);
    }

    private static bool CancellationIntegrationEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("ATOMBOX_TEST_OSS_CANCELLATION"),
            "1",
            StringComparison.Ordinal);
    }

    private sealed class CancelOnFirstProgress : IProgress<TransferProgress>
    {
        private readonly CancellationTokenSource _cancellation;

        public CancelOnFirstProgress(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public bool WasReported { get; private set; }

        public void Report(TransferProgress value)
        {
            WasReported = true;
            _cancellation.Cancel();
        }
    }

    private sealed class GatedReadStream : MemoryStream
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GatedReadStream(byte[] payload)
            : base(payload, writable: false)
        {
        }

        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release()
        {
            _release.TrySetResult();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadStarted.TrySetResult();
            _release.Task.ConfigureAwait(false).GetAwaiter().GetResult();
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            ReadStarted.TrySetResult();
            _release.Task.ConfigureAwait(false).GetAwaiter().GetResult();
            return base.Read(buffer);
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
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
