using System.Net;
using Qiniu.Storage;
using Qiniu.Util;
using AtomBox.Providers.ObjectStorage.QiniuKodo;

namespace AtomBox.Providers.Tests;

public sealed class QiniuKodoSdkClientDownloadTests
{
    [Fact]
    public async Task GetObjectAsync_ReturnsResponseStreamWithoutBufferingWholeObject()
    {
        var responseStream = new TrackingMemoryStream(new byte[1024]);
        using var handler = new StaticResponseHandler(responseStream);
        using var httpClient = new HttpClient(handler);
        using var client = new QiniuKodoSdkClient(
            new Mac("access-key", "secret-key"),
            new Config(),
            "https://download.example.com",
            "access-key",
            "secret-key",
            httpClient);

        var remoteObject = await client.GetObjectAsync("bucket", "file.bin");

        Assert.Equal(0, responseStream.BytesRead);
        Assert.Equal(1024, remoteObject.ContentLength);
        var buffer = new byte[128];
        var read = await remoteObject.Content.ReadAsync(buffer);
        Assert.Equal(128, read);
        Assert.InRange(responseStream.BytesRead, 1, 1023);

        remoteObject.Dispose();
        Assert.True(responseStream.WasDisposed);
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly Stream _content;

        public StaticResponseHandler(Stream content)
        {
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = new StreamContent(_content);
            content.Headers.ContentLength = _content.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class TrackingMemoryStream : MemoryStream
    {
        public TrackingMemoryStream(byte[] buffer)
            : base(buffer)
        {
        }

        public long BytesRead { get; private set; }

        public bool WasDisposed { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = base.Read(buffer);
            BytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
