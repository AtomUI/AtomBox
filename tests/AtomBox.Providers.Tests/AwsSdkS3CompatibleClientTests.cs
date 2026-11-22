using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.Tests;

public sealed class AwsSdkS3CompatibleClientTests
{
    [Fact]
    public async Task CreateBucketAsync_AppliesPrivateAclAndCallerToken()
    {
        using var amazonClient = new CapturingAmazonS3Client();
        using var client = new AwsSdkS3CompatibleClient(
            amazonClient,
            new AwsSdkS3ClientOptions(CreateBucketAcl: S3CannedACL.Private));
        using var cancellation = new CancellationTokenSource();

        await client.CreateBucketAsync("bucket-a", cancellation.Token);

        Assert.NotNull(amazonClient.PutBucketRequest);
        Assert.Equal("bucket-a", amazonClient.PutBucketRequest.BucketName);
        Assert.Equal(S3CannedACL.Private, amazonClient.PutBucketRequest.CannedACL);
        Assert.Equal(cancellation.Token, amazonClient.PutBucketToken);
    }

    [Fact]
    public async Task PutObjectAsync_AppliesCompatibilityFlagsAndCallerToken()
    {
        using var amazonClient = new CapturingAmazonS3Client();
        using var client = new AwsSdkS3CompatibleClient(
            amazonClient,
            new AwsSdkS3ClientOptions(UseChunkEncoding: false, DisablePayloadSigning: true));
        using var cancellation = new CancellationTokenSource();
        await using var content = new MemoryStream([1, 2, 3]);

        await client.PutObjectAsync("bucket-a", "file.bin", content, cancellation.Token);

        Assert.NotNull(amazonClient.PutObjectRequest);
        Assert.False(amazonClient.PutObjectRequest.UseChunkEncoding);
        Assert.True(amazonClient.PutObjectRequest.DisablePayloadSigning);
        Assert.Equal(cancellation.Token, amazonClient.PutObjectToken);
    }

    [Fact]
    public async Task MultipartAsync_AppliesCompatibilityFlagsAndCompletes()
    {
        using var amazonClient = new CapturingAmazonS3Client();
        using var client = new AwsSdkS3CompatibleClient(
            amazonClient,
            new AwsSdkS3ClientOptions(UseChunkEncoding: false, DisablePayloadSigning: true));
        await using var content = new MemoryStream([1, 2, 3]);

        await client.PutObjectMultipartAsync("bucket-a", "file.bin", content, content.Length);

        Assert.NotNull(amazonClient.UploadPartRequest);
        Assert.False(amazonClient.UploadPartRequest.UseChunkEncoding);
        Assert.True(amazonClient.UploadPartRequest.DisablePayloadSigning);
        Assert.True(amazonClient.CompleteCalled);
        Assert.False(amazonClient.AbortCalled);
    }

    [Fact]
    public async Task MultipartAsync_CancellationDuringLocalRead_UsesIndependentBoundedAbortToken()
    {
        using var amazonClient = new CapturingAmazonS3Client();
        using var client = new AwsSdkS3CompatibleClient(
            amazonClient,
            new AwsSdkS3ClientOptions(CleanupTimeout: TimeSpan.FromSeconds(1)));
        await using var content = new CancellationBlockingReadStream();
        using var cancellation = new CancellationTokenSource();

        var operation = client.PutObjectMultipartAsync(
            "bucket-a",
            "cancel.bin",
            content,
            1024,
            cancellationToken: cancellation.Token);
        await content.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.True(amazonClient.AbortCalled);
        Assert.False(amazonClient.AbortToken.IsCancellationRequested);
        Assert.False(amazonClient.CompleteCalled);
        Assert.Null(amazonClient.UploadPartRequest);
    }

    private sealed class CapturingAmazonS3Client : AmazonS3Client
    {
        public CapturingAmazonS3Client()
            : base(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
        {
        }

        public PutObjectRequest? PutObjectRequest { get; private set; }

        public CancellationToken PutObjectToken { get; private set; }

        public PutBucketRequest? PutBucketRequest { get; private set; }

        public CancellationToken PutBucketToken { get; private set; }

        public UploadPartRequest? UploadPartRequest { get; private set; }

        public bool CompleteCalled { get; private set; }

        public bool AbortCalled { get; private set; }

        public CancellationToken AbortToken { get; private set; }

        public override Task<PutBucketResponse> PutBucketAsync(
            PutBucketRequest request,
            CancellationToken cancellationToken = default)
        {
            PutBucketRequest = request;
            PutBucketToken = cancellationToken;
            return Task.FromResult(new PutBucketResponse());
        }

        public override Task<PutObjectResponse> PutObjectAsync(
            PutObjectRequest request,
            CancellationToken cancellationToken = default)
        {
            PutObjectRequest = request;
            PutObjectToken = cancellationToken;
            return Task.FromResult(new PutObjectResponse());
        }

        public override Task<InitiateMultipartUploadResponse> InitiateMultipartUploadAsync(
            InitiateMultipartUploadRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
        }

        public override Task<UploadPartResponse> UploadPartAsync(
            UploadPartRequest request,
            CancellationToken cancellationToken = default)
        {
            UploadPartRequest = request;
            return Task.FromResult(new UploadPartResponse { ETag = "etag-1" });
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
            AbortToken = cancellationToken;
            return Task.FromResult(new AbortMultipartUploadResponse());
        }
    }

    private sealed class CancellationBlockingReadStream : Stream
    {
        public TaskCompletionSource<bool> ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
