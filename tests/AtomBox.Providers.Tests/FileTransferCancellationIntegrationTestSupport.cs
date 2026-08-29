using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Providers.Tests;

internal static class FileTransferCancellationIntegrationTestSupport
{
    private const long TransferLength = 8L * 1024 * 1024;

    public static async Task VerifyAsync(IStorageProvider provider, string testRoot)
    {
        var rootPath = new RemotePath(testRoot, RemotePathKind.Folder);
        var canceledUploadPath = new RemotePath($"{testRoot}/canceled-upload.bin", RemotePathKind.ObjectPath);
        var downloadSourcePath = new RemotePath($"{testRoot}/download-source.bin", RemotePathKind.ObjectPath);

        var createRoot = await provider.CreateFolderAsync(rootPath);
        Assert.True(createRoot.IsSuccess, FormatError("Expected cancellation test folder creation to succeed.", createRoot.Error));

        try
        {
            await VerifyCanceledUploadAsync(provider, rootPath, canceledUploadPath);
            await VerifyCanceledDownloadAsync(provider, downloadSourcePath);
        }
        finally
        {
            await DeleteTreeBestEffortAsync(provider, rootPath);
        }

        var parentPath = rootPath.GetParent() ?? RemotePath.Root;
        var parentList = await provider.ListAsync(parentPath);
        Assert.True(parentList.IsSuccess, FormatError("Expected test parent list after cleanup to succeed.", parentList.Error));
        Assert.DoesNotContain(parentList.GetValueOrThrow(), item => item.Name == rootPath.Name);
    }

    private static async Task VerifyCanceledUploadAsync(
        IStorageProvider provider,
        RemotePath rootPath,
        RemotePath targetPath)
    {
        await using var source = new SlowPatternReadStream(TransferLength, TimeSpan.FromMilliseconds(12));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var progress = new SignalingProgress();

        var uploadTask = provider.UploadAsync(
            targetPath,
            source,
            source.Length,
            progress,
            cancellation.Token);
        await progress.Started.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        var uploadResult = await uploadTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(uploadResult.IsFailure, "Expected the running upload to be canceled.");
        Assert.Equal(StorageErrorCategory.Canceled, uploadResult.Error?.Category);

        var listResult = await provider.ListAsync(rootPath);
        Assert.True(listResult.IsSuccess, FormatError("Expected list after canceled upload to succeed.", listResult.Error));
        Assert.DoesNotContain(
            listResult.GetValueOrThrow(),
            item => item.Name == targetPath.Name || IsAtomBoxTemporaryFile(item.Name));
    }

    private static async Task VerifyCanceledDownloadAsync(
        IStorageProvider provider,
        RemotePath sourcePath)
    {
        await using (var source = new SlowPatternReadStream(TransferLength, TimeSpan.Zero))
        {
            var uploadResult = await provider.UploadAsync(sourcePath, source, source.Length);
            Assert.True(uploadResult.IsSuccess, FormatError("Expected download source upload to succeed.", uploadResult.Error));
        }

        await using var destination = new SlowDiscardWriteStream(TimeSpan.FromMilliseconds(12));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var progress = new SignalingProgress();

        var downloadTask = provider.DownloadAsync(sourcePath, destination, progress, cancellation.Token);
        await progress.Started.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        var downloadResult = await downloadTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(downloadResult.IsFailure, "Expected the running download to be canceled.");
        Assert.Equal(StorageErrorCategory.Canceled, downloadResult.Error?.Category);
        Assert.True(destination.BytesWritten < TransferLength, "Canceled download unexpectedly wrote the complete payload.");
    }

    private static async Task DeleteTreeBestEffortAsync(IStorageProvider provider, RemotePath folderPath)
    {
        var listResult = await provider.ListAsync(folderPath);
        if (listResult.IsSuccess)
        {
            foreach (var item in listResult.GetValueOrThrow())
            {
                if (item.Kind == RemoteItemKind.Folder)
                {
                    await DeleteTreeBestEffortAsync(provider, item.Path);
                }
                else
                {
                    await provider.DeleteAsync(item.Path);
                }
            }
        }

        await provider.DeleteAsync(folderPath);
    }

    private static bool IsAtomBoxTemporaryFile(string name)
    {
        return name.StartsWith(".atombox-upload-", StringComparison.Ordinal) ||
               name.StartsWith(".atombox-backup-", StringComparison.Ordinal);
    }

    private static string FormatError(string message, StorageError? error)
    {
        return error is null
            ? message
            : $"{message} Code={error.Code}; Category={error.Category}; Retryable={error.IsRetryable}; ProviderErrorCode={error.ProviderErrorCode}; Message={error.Message}";
    }

    private sealed class SignalingProgress : IProgress<TransferProgress>
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Report(TransferProgress value)
        {
            if (value.BytesTransferred > 0)
            {
                _started.TrySetResult();
            }
        }
    }

    private sealed class SlowPatternReadStream : Stream
    {
        private readonly long _length;
        private readonly TimeSpan _delay;
        private long _position;

        public SlowPatternReadStream(long length, TimeSpan delay)
        {
            _length = length;
            _delay = delay;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, _length);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_delay > TimeSpan.Zero)
            {
                Thread.Sleep(_delay);
            }

            return Fill(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (_delay > TimeSpan.Zero)
            {
                Thread.Sleep(_delay);
            }

            return Fill(buffer);
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Fill(buffer.Span);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            Position = target;
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Fill(Span<byte> buffer)
        {
            var count = (int)Math.Min(buffer.Length, _length - _position);
            for (var index = 0; index < count; index++)
            {
                buffer[index] = (byte)((_position + index) % 251);
            }

            _position += count;
            return count;
        }
    }

    private sealed class SlowDiscardWriteStream : Stream
    {
        private readonly TimeSpan _delay;

        public SlowDiscardWriteStream(TimeSpan delay)
        {
            _delay = delay;
        }

        public long BytesWritten { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => BytesWritten;

        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Thread.Sleep(_delay);
            BytesWritten += count;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            BytesWritten += buffer.Length;
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
