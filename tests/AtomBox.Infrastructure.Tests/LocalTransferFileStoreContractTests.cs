using AtomBox.Core.Errors;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Infrastructure.Tests;

public sealed class LocalTransferFileStoreContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));
    private readonly LocalTransferFileStore _store = new();

    [Fact]
    public async Task OpenRead_FileNotFound_ReturnsNotFound()
    {
        var result = await _store.OpenReadAsync(new LocalPath(Path.Combine(_root, "missing.txt")));
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task OpenRead_EmptyPath_ReturnsValidation()
    {
        var result = await _store.OpenReadAsync(default(LocalPath));
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task OpenRead_CancelledToken_ReturnsCanceled()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await _store.OpenReadAsync(new LocalPath(Path.Combine(_root, "any.txt")), cts.Token);
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Canceled, result.Error?.Category);
    }

    [Fact]
    public async Task OpenWrite_CancelledToken_ReturnsCanceled()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await _store.OpenWriteAsync(new LocalPath(Path.Combine(_root, "any.txt")), cts.Token);
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Canceled, result.Error?.Category);
    }

    [Fact]
    public async Task OpenRead_ZeroLengthFile_Succeeds()
    {
        var path = Path.Combine(_root, "empty.bin");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(path, []);

        await using var handle = (await _store.OpenReadAsync(new LocalPath(path))).GetValueOrThrow();
        Assert.Equal(0, handle.Length);
        Assert.Equal(0, await handle.Stream.ReadAsync(new byte[1]));
    }

    [Fact]
    public async Task OpenWrite_CreatesNestedDirectories()
    {
        var path = Path.Combine(_root, "a", "b", "c", "out.bin");
        var result = await _store.OpenWriteAsync(new LocalPath(path));
        Assert.True(result.IsSuccess);
        await using var handle = result.GetValueOrThrow();
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
    }

    [Fact]
    public async Task OpenWrite_OnExistingFile_ReturnsConflict()
    {
        var path = Path.Combine(_root, "existing.txt");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path, "data");

        var result = await _store.OpenWriteAsync(new LocalPath(path));
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
    }

    [Fact]
    public async Task OpenWrite_RenamePolicy_CreatesSiblingWhenTargetExists()
    {
        var path = Path.Combine(_root, "existing.txt");
        var renamedPath = Path.Combine(_root, "existing (1).txt");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path, "data");

        var result = await _store.OpenWriteAsync(new LocalPath(path), TransferOverwritePolicy.Rename);

        Assert.True(result.IsSuccess);
        await using var handle = result.GetValueOrThrow();
        await handle.Stream.WriteAsync(new byte[] { 1, 2, 3 });
        await handle.DisposeAsync();
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(renamedPath));
    }

    [Fact]
    public async Task OpenWrite_OverwritePolicy_ReplacesExistingFile()
    {
        var path = Path.Combine(_root, "existing.txt");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path, "data");

        var result = await _store.OpenWriteAsync(new LocalPath(path), TransferOverwritePolicy.Overwrite);

        Assert.True(result.IsSuccess);
        await using var handle = result.GetValueOrThrow();
        await handle.Stream.WriteAsync(new byte[] { 1, 2, 3 });
        await handle.DisposeAsync();
        Assert.Equal(3, new FileInfo(path).Length);
    }

    [Fact]
    public async Task OpenWrite_EmptyPath_ReturnsValidation()
    {
        var result = await _store.OpenWriteAsync(default(LocalPath));
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task WriteThenRead_Roundtrips()
    {
        var path = Path.Combine(_root, "roundtrip.bin");
        var content = new byte[] { 1, 2, 3, 4, 5 };

        await using (var writeHandle = (await _store.OpenWriteAsync(new LocalPath(path))).GetValueOrThrow())
        {
            await writeHandle.Stream.WriteAsync(content);
        }

        await using var readHandle = (await _store.OpenReadAsync(new LocalPath(path))).GetValueOrThrow();
        var readBuffer = new byte[content.Length];
        var bytesRead = await readHandle.Stream.ReadAsync(readBuffer);
        Assert.Equal(content.Length, bytesRead);
        Assert.Equal(content, readBuffer);
        Assert.Equal(content.Length, readHandle.Length);
    }

    [Fact]
    public async Task OpenRead_LargeFile_ReturnsCorrectLength()
    {
        var path = Path.Combine(_root, "length-test.bin");
        Directory.CreateDirectory(_root);
        var content = new byte[65536];
        new Random(42).NextBytes(content);
        await File.WriteAllBytesAsync(path, content);

        await using var handle = (await _store.OpenReadAsync(new LocalPath(path))).GetValueOrThrow();
        Assert.Equal(65536, handle.Length);
    }

    [Fact]
    public async Task OpenWrite_CreatesFileWithCreateNewSemantics()
    {
        var path = Path.Combine(_root, "newfile.txt");
        var result = await _store.OpenWriteAsync(new LocalPath(path));
        Assert.True(result.IsSuccess);
        await using var handle = result.GetValueOrThrow();
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task OpenRead_DirectoryPath_ReturnsAuthorization()
    {
        var dir = Path.Combine(_root, "subdir");
        Directory.CreateDirectory(dir);

        var result = await _store.OpenReadAsync(new LocalPath(dir));
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Authorization, result.Error?.Category);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
