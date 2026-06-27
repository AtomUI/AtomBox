using AtomBox.Core.Capabilities;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Tests;

public sealed class RemotePathTests
{
    [Fact]
    public void Constructor_NormalizesDuplicateSeparatorsAndBackslashes()
    {
        var path = new RemotePath(@"//bucket\\folder///file.txt");

        Assert.Equal("bucket/folder/file.txt", path.Value);
        Assert.Equal("file.txt", path.Name);
        Assert.False(path.IsRoot);
    }

    [Fact]
    public void Root_CombineAndParent_KeepRemotePathSemantics()
    {
        var root = RemotePath.Root;
        var child = root.Combine("folder").Combine("file.txt");

        Assert.Equal("folder/file.txt", child.Value);
        Assert.Equal("file.txt", child.Name);
        Assert.Equal("folder", child.GetParent()?.Value);
        Assert.True(child.GetParent()?.GetParent()?.IsRoot);
    }

    [Fact]
    public void DefaultValue_IsSafeRootLikePath()
    {
        var path = default(RemotePath);

        Assert.True(path.IsRoot);
        Assert.Equal(string.Empty, path.ToString());
        Assert.Null(path.GetParent());
    }

    [Fact]
    public async Task StorageProvider_DefaultPageList_UsesOpaqueOffsetCursors()
    {
        IStorageProvider provider = new PagingStorageProvider(
        [
            CreateItem("a.txt"),
            CreateItem("b.txt"),
            CreateItem("c.txt")
        ]);

        var first = await provider.ListPageAsync(RemotePath.Root, new RemotePageRequest(2));
        var second = await provider.ListPageAsync(
            RemotePath.Root,
            new RemotePageRequest(2, first.GetValueOrThrow().NextCursor));

        Assert.True(first.IsSuccess);
        Assert.Equal(["a.txt", "b.txt"], first.GetValueOrThrow().Items.Select(item => item.Name).ToArray());
        Assert.False(first.GetValueOrThrow().HasPreviousPage);
        Assert.True(first.GetValueOrThrow().HasNextPage);
        Assert.True(second.IsSuccess);
        Assert.Equal(["c.txt"], second.GetValueOrThrow().Items.Select(item => item.Name).ToArray());
        Assert.True(second.GetValueOrThrow().HasPreviousPage);
        Assert.False(second.GetValueOrThrow().HasNextPage);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task StorageProvider_DefaultMutationExtensions_ReturnNotSupportedOrValidation()
    {
        IStorageProvider provider = new PagingStorageProvider([]);

        var createFolder = await provider.CreateFolderAsync(new RemotePath("folder", RemotePathKind.Folder));
        var move = await provider.MoveAsync(new RemotePath("a.txt"), new RemotePath("b.txt"));
        var renameRoot = await provider.RenameAsync(RemotePath.Root, "new-name");
        var renameInvalidName = await provider.RenameAsync(new RemotePath("a.txt"), "folder/b.txt");

        Assert.Equal(StorageErrorCode.OperationNotSupported, createFolder.Error?.Code);
        Assert.Equal(StorageErrorCode.OperationNotSupported, move.Error?.Code);
        Assert.Equal(StorageErrorCategory.Validation, renameRoot.Error?.Category);
        Assert.Equal(StorageErrorCategory.Validation, renameInvalidName.Error?.Category);
        await provider.DisposeAsync();
    }

    private static RemoteItem CreateItem(string name)
    {
        return new RemoteItem(name, new RemotePath(name), RemoteItemKind.File, 1, null);
    }

    private sealed class PagingStorageProvider : IStorageProvider
    {
        private readonly IReadOnlyList<RemoteItem> _items;

        public PagingStorageProvider(IReadOnlyList<RemoteItem> items)
        {
            _items = items;
        }

        public StorageCapabilitySet Capabilities => StorageCapabilitySet.Empty;

        public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
            RemotePath path,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Success(_items));
        }

        public Task<OperationResult> DeleteAsync(RemotePath path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> UploadAsync(
            RemotePath path,
            Stream content,
            long? contentLength,
            IProgress<TransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> DownloadAsync(
            RemotePath path,
            Stream destination,
            IProgress<TransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
