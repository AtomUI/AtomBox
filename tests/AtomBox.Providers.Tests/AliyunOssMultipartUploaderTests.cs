using Aliyun.OSS;
using AtomBox.Providers.ObjectStorage;
using AtomBox.Providers.ObjectStorage.AliyunOss;

namespace AtomBox.Providers.Tests;

public sealed class AliyunOssMultipartUploaderTests
{
    [Fact]
    public void Upload_CompletesExplicitMultipartUploadInOrderedParts()
    {
        var operations = new FakeMultipartOperations();
        var uploader = new AliyunOssMultipartUploader(operations);
        var contentLength = ObjectStorageUploadPolicy.PartSize + 3;
        using var content = new MemoryStream(new byte[contentLength]);
        (long Completed, long Total) latestProgress = default;

        uploader.Upload(
            "bucket-a",
            "folder/file.bin",
            content,
            contentLength,
            (completed, total) => latestProgress = (completed, total),
            CancellationToken.None);

        Assert.Equal([ObjectStorageUploadPolicy.PartSize, 3], operations.UploadedPartSizes);
        Assert.True(operations.CompleteCalled);
        Assert.False(operations.AbortCalled);
        Assert.Equal([1, 2], operations.CompletedParts.Select(part => part.PartNumber));
        Assert.Equal((contentLength, contentLength), latestProgress);
    }

    [Fact]
    public void Upload_AbortsMultipartUpload_WhenCancellationOccursDuringPartTransfer()
    {
        using var cancellation = new CancellationTokenSource();
        var operations = new FakeMultipartOperations
        {
            CancelDuringFirstPart = cancellation
        };
        var uploader = new AliyunOssMultipartUploader(operations);
        var contentLength = ObjectStorageUploadPolicy.PartSize + 1;
        using var content = new MemoryStream(new byte[contentLength]);

        Assert.Throws<OperationCanceledException>(() => uploader.Upload(
            "bucket-a",
            "folder/file.bin",
            content,
            contentLength,
            progress: null,
            cancellation.Token));

        Assert.True(operations.AbortCalled);
        Assert.False(operations.CompleteCalled);
        Assert.Equal("upload-1", operations.AbortedUploadId);
    }

    [Fact]
    public void Upload_AbortsMultipartUpload_AndPreservesOriginalPartFailure()
    {
        var expected = new IOException("part upload failed");
        var operations = new FakeMultipartOperations
        {
            UploadFailure = expected,
            AbortFailure = new InvalidOperationException("abort failed")
        };
        var uploader = new AliyunOssMultipartUploader(operations);
        using var content = new MemoryStream(new byte[ObjectStorageUploadPolicy.MultipartThreshold]);

        var actual = Assert.Throws<IOException>(() => uploader.Upload(
            "bucket-a",
            "folder/file.bin",
            content,
            content.Length,
            progress: null,
            CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.True(operations.AbortCalled);
        Assert.False(operations.CompleteCalled);
    }

    private sealed class FakeMultipartOperations : IAliyunOssMultipartOperations
    {
        public CancellationTokenSource? CancelDuringFirstPart { get; init; }

        public Exception? UploadFailure { get; init; }

        public Exception? AbortFailure { get; init; }

        public List<long> UploadedPartSizes { get; } = [];

        public IReadOnlyList<PartETag> CompletedParts { get; private set; } = [];

        public bool CompleteCalled { get; private set; }

        public bool AbortCalled { get; private set; }

        public string? AbortedUploadId { get; private set; }

        public string Initiate(string bucketName, string key)
        {
            return "upload-1";
        }

        public PartETag UploadPart(
            string bucketName,
            string key,
            string uploadId,
            int partNumber,
            long partSize,
            Stream content,
            EventHandler<StreamTransferProgressArgs>? progress)
        {
            UploadedPartSizes.Add(partSize);
            if (partNumber == 1 && CancelDuringFirstPart is not null)
            {
                CancelDuringFirstPart.Cancel();
                progress?.Invoke(
                    this,
                    new StreamTransferProgressArgs(1, 1, partSize));
            }

            if (UploadFailure is not null)
            {
                throw UploadFailure;
            }

            var buffer = new byte[8192];
            long transferred = 0;
            while (transferred < partSize)
            {
                var read = content.Read(
                    buffer,
                    0,
                    (int)Math.Min(buffer.Length, partSize - transferred));
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                transferred += read;
            }

            progress?.Invoke(
                this,
                new StreamTransferProgressArgs(transferred, transferred, partSize));
            return new PartETag(partNumber, $"etag-{partNumber}");
        }

        public void Complete(
            string bucketName,
            string key,
            string uploadId,
            IReadOnlyList<PartETag> parts)
        {
            CompleteCalled = true;
            CompletedParts = parts.ToArray();
        }

        public void Abort(string bucketName, string key, string uploadId)
        {
            AbortCalled = true;
            AbortedUploadId = uploadId;
            if (AbortFailure is not null)
            {
                throw AbortFailure;
            }
        }
    }
}
