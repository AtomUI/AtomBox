using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using AtomBox.Core.Errors;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage;
using AtomBox.Providers.ObjectStorage.GenericS3;

namespace AtomBox.Providers.Tests;

public sealed class AwsSdkS3CompatibleClientCancellationTests
{
    [Fact]
    public async Task PutObjectAsync_ForwardsCancellationTokenToAwsRequest()
    {
        using var amazonClient = new RecordingAmazonS3Client();
        using var client = new AwsSdkS3CompatibleClient(amazonClient);
        using var source = new MemoryStream([1, 2, 3]);
        using var cancellation = new CancellationTokenSource();

        var operation = client.PutObjectAsync("bucket", "file.bin", source, cancellation.Token);
        await amazonClient.PutObjectStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(cancellation.Token, amazonClient.PutObjectCancellationToken);
    }

    [Fact]
    public async Task GetObjectAsync_ForwardsCancellationTokenToAwsRequest()
    {
        using var amazonClient = new RecordingAmazonS3Client();
        using var client = new AwsSdkS3CompatibleClient(amazonClient);
        using var cancellation = new CancellationTokenSource();

        var operation = client.GetObjectAsync("bucket", "file.bin", cancellation.Token);
        await amazonClient.GetObjectStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(cancellation.Token, amazonClient.GetObjectCancellationToken);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_CancelsPartAndAbortsWithIndependentToken()
    {
        using var amazonClient = new RecordingAmazonS3Client();
        using var client = new AwsSdkS3CompatibleClient(amazonClient);
        using var source = new MemoryStream(new byte[ObjectStorageUploadPolicy.MultipartThreshold]);
        using var cancellation = new CancellationTokenSource();

        var operation = client.PutObjectMultipartAsync(
            "bucket",
            "large.bin",
            source,
            source.Length,
            cancellationToken: cancellation.Token);
        await amazonClient.UploadPartStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.True(amazonClient.AbortCalled);
        Assert.False(amazonClient.AbortCancellationToken.IsCancellationRequested);
        Assert.False(amazonClient.CompleteCalled);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_PreservesCanceledCategoryWhenAbortFails()
    {
        using var amazonClient = new RecordingAmazonS3Client { AbortException = new IOException("abort failed") };
        using var client = new AwsSdkS3CompatibleClient(amazonClient);
        using var source = new MemoryStream(new byte[ObjectStorageUploadPolicy.MultipartThreshold]);
        using var cancellation = new CancellationTokenSource();

        var operation = client.PutObjectMultipartAsync(
            "bucket",
            "large.bin",
            source,
            source.Length,
            cancellationToken: cancellation.Token);
        await amazonClient.UploadPartStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<ProviderOperationCanceledException>(() => operation);
        var error = ProviderErrorMapper.FromException(exception);
        Assert.Equal(StorageErrorCategory.Canceled, error.Category);
        Assert.Contains("incomplete parts may remain", error.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingAmazonS3Client : AmazonS3Client
    {
        public RecordingAmazonS3Client()
            : base(new AnonymousAWSCredentials(), new AmazonS3Config
            {
                ServiceURL = "http://127.0.0.1:9000",
                ForcePathStyle = true
            })
        {
        }

        public TaskCompletionSource PutObjectStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource GetObjectStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource UploadPartStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken PutObjectCancellationToken { get; private set; }

        public CancellationToken GetObjectCancellationToken { get; private set; }

        public CancellationToken AbortCancellationToken { get; private set; }

        public bool AbortCalled { get; private set; }

        public bool CompleteCalled { get; private set; }

        public Exception? AbortException { get; init; }

        public override async Task<PutObjectResponse> PutObjectAsync(
            PutObjectRequest request,
            CancellationToken cancellationToken = default)
        {
            PutObjectCancellationToken = cancellationToken;
            PutObjectStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new PutObjectResponse();
        }

        public override async Task<GetObjectResponse> GetObjectAsync(
            GetObjectRequest request,
            CancellationToken cancellationToken = default)
        {
            GetObjectCancellationToken = cancellationToken;
            GetObjectStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new GetObjectResponse();
        }

        public override Task<InitiateMultipartUploadResponse> InitiateMultipartUploadAsync(
            InitiateMultipartUploadRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
        }

        public override async Task<UploadPartResponse> UploadPartAsync(
            UploadPartRequest request,
            CancellationToken cancellationToken = default)
        {
            UploadPartStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new UploadPartResponse { ETag = "etag-1" };
        }

        public override Task<CompleteMultipartUploadResponse> CompleteMultipartUploadAsync(
            CompleteMultipartUploadRequest request,
            CancellationToken cancellationToken = default)
        {
            CompleteCalled = true;
            return Task.FromResult(new CompleteMultipartUploadResponse());
        }

        public override Task<AbortMultipartUploadResponse> AbortMultipartUploadAsync(
            AbortMultipartUploadRequest request,
            CancellationToken cancellationToken = default)
        {
            AbortCalled = true;
            AbortCancellationToken = cancellationToken;
            return AbortException is null
                ? Task.FromResult(new AbortMultipartUploadResponse())
                : Task.FromException<AbortMultipartUploadResponse>(AbortException);
        }
    }
}
