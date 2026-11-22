using System.Net;
using AtomBox.Providers.FileTransfer.WebDav;

namespace AtomBox.Providers.Tests;

public sealed class WebDavHttpClientAdapterCancellationTests
{
    [Fact]
    public async Task SendAsync_PropagatesCallerCancellationToHttpHandler()
    {
        var handler = new BlockingRequestHandler();
        await using var client = CreateClient(handler);
        using var cancellationSource = new CancellationTokenSource();

        var requestTask = client.ListDirectoryAsync("/", cancellationSource.Token);
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => requestTask.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.True(handler.ObservedToken.CanBeCanceled);
    }

    [Fact]
    public async Task UploadFileAsync_PropagatesCallerCancellationToContentStream()
    {
        var handler = new CopyRequestContentHandler();
        await using var client = CreateClient(handler);
        await using var source = new CancellationBlockingReadStream();
        using var cancellationSource = new CancellationTokenSource();

        var uploadTask = client.UploadFileAsync(
            source,
            "/file.bin",
            cancellationToken: cancellationSource.Token);
        await source.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => uploadTask.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.True(source.ObservedToken.CanBeCanceled);
    }

    [Fact]
    public async Task DownloadFileAsync_PropagatesCallerCancellationToResponseStream()
    {
        await using var source = new CancellationBlockingReadStream();
        var handler = new StreamResponseHandler(source);
        await using var client = CreateClient(handler);
        await using var destination = new MemoryStream();
        using var cancellationSource = new CancellationTokenSource();

        var downloadTask = client.DownloadFileAsync(
            "/file.bin",
            destination,
            cancellationToken: cancellationSource.Token);
        await source.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => downloadTask.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.True(source.ObservedToken.CanBeCanceled);
    }

    [Fact]
    public async Task SendAsync_MapsNonCallerCancellationToTimeout()
    {
        await using var client = CreateClient(new NonCallerCancellationHandler());

        await Assert.ThrowsAsync<TimeoutException>(
            () => client.DeleteAsync("/file.bin"));
    }

    private static WebDavHttpClientAdapter CreateClient(HttpMessageHandler handler)
    {
        return new WebDavHttpClientAdapter(
            new Uri("http://localhost/dav/"),
            null,
            null,
            timeoutSeconds: 30,
            handler);
    }

    private sealed class BlockingRequestHandler : HttpMessageHandler
    {
        public TaskCompletionSource<bool> RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ObservedToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ObservedToken = cancellationToken;
            RequestStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The blocking request unexpectedly completed.");
        }
    }

    private sealed class CopyRequestContentHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request.Content);
            await request.Content.CopyToAsync(Stream.Null, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }

    private sealed class StreamResponseHandler(Stream source) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(source)
            });
        }
    }

    private sealed class NonCallerCancellationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(new OperationCanceledException());
        }
    }

    private sealed class CancellationBlockingReadStream : Stream
    {
        public TaskCompletionSource<bool> ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ObservedToken { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("The synchronous read path must not be used.");
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ObservedToken = cancellationToken;
            ReadStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
