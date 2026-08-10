using AtomBox.Core.Errors;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using System.Security.Cryptography;
using System.Text;

namespace AtomBox.EndToEnd.Tests;

public sealed class GenericS3ProviderEndToEndTests
{
    [Fact]
    [Trait("Category", "ManualIntegration")]
    public async Task Provider_AllPublishedObjectStorageOperations_WorkAgainstMinio()
    {
        await using var context = await GenericS3EndToEndContext.CreateAsync();
        if (context is null)
        {
            return;
        }

        await context.CreateAndDeleteTemporaryBucketAsync();
        await using var provider = await context.CreateProviderAsync();

        var bucketList = await provider.ListAsync(RemotePath.Root);
        GenericS3EndToEndContext.AssertSuccess(bucketList, "List MinIO buckets");
        Assert.Contains(bucketList.GetValueOrThrow(), item =>
            item.Kind == RemoteItemKind.Bucket && item.Name == context.Environment.Bucket);

        var runFolder = context.RunFolderPath;
        GenericS3EndToEndContext.AssertSuccess(
            await provider.CreateFolderAsync(runFolder),
            "Create E2E run folder marker");

        await VerifyObjectVariantsAndProgressAsync(context, provider);
        await VerifyPagingSearchAndPreviousPageAsync(context, provider);
        await VerifyFolderRenameMoveAndContinuationAsync(context, provider);
        await VerifyNotFoundMappingAsync(context, provider);

        await context.WriteEvidenceManifestAsync(new Dictionary<string, object?>
        {
            ["providerOperations"] = "passed",
            ["pagingObjectCount"] = 205,
            ["folderMoveObjectCount"] = 1001,
            ["multipartBytes"] = 16 * 1024 * 1024
        });
    }

    private static async Task VerifyObjectVariantsAndProgressAsync(
        GenericS3EndToEndContext context,
        AtomBox.Core.Providers.IStorageProvider provider)
    {
        var emptyPath = context.ObjectPath("objects/empty.bin");
        await context.UploadAsync(provider, emptyPath, []);
        Assert.Empty(await context.DownloadAsync(provider, emptyPath));

        var specialPath = context.ObjectPath("objects/中文 空格 # % +.txt");
        var specialPayload = Encoding.UTF8.GetBytes("AtomBox 通用 S3 真实性测试\nline-2");
        await context.UploadAsync(provider, specialPath, specialPayload);
        Assert.Equal(specialPayload, await context.DownloadAsync(provider, specialPath));

        var overwritePath = context.ObjectPath("objects/overwrite.txt");
        await context.UploadAsync(provider, overwritePath, "first"u8.ToArray());
        var overwritePayload = "second-version"u8.ToArray();
        await context.UploadAsync(provider, overwritePath, overwritePayload);
        Assert.Equal(overwritePayload, await context.DownloadAsync(provider, overwritePath));

        var payload = new byte[16 * 1024 * 1024];
        new Random(1001).NextBytes(payload);
        var multipartPath = context.ObjectPath("objects/multipart-16mb.bin");
        var uploadProgress = new RecordingProgress();
        await using (var source = new MemoryStream(payload, writable: false))
        {
            var upload = await provider.UploadAsync(multipartPath, source, source.Length, uploadProgress);
            GenericS3EndToEndContext.AssertSuccess(upload, "Multipart upload with progress");
        }

        AssertProgress(uploadProgress.Values, payload.LongLength, "upload");
        var downloadProgress = new RecordingProgress();
        await using var destination = new MemoryStream();
        var download = await provider.DownloadAsync(multipartPath, destination, downloadProgress);
        GenericS3EndToEndContext.AssertSuccess(download, "Multipart object download with progress");
        AssertProgress(downloadProgress.Values, payload.LongLength, "download");
        Assert.Equal(SHA256.HashData(payload), SHA256.HashData(destination.ToArray()));

        var list = await provider.ListAsync(context.FolderPath("objects"));
        GenericS3EndToEndContext.AssertSuccess(list, "List special object folder");
        Assert.Contains(list.GetValueOrThrow(), item => item.Name == "中文 空格 # % +.txt");
    }

    private static async Task VerifyPagingSearchAndPreviousPageAsync(
        GenericS3EndToEndContext context,
        AtomBox.Core.Providers.IStorageProvider provider)
    {
        var folder = context.FolderPath("paging");
        GenericS3EndToEndContext.AssertSuccess(await provider.CreateFolderAsync(folder), "Create paging folder");

        for (var index = 0; index < 205; index++)
        {
            var name = index % 20 == 0 ? $"match-{index:D4}.txt" : $"item-{index:D4}.txt";
            await context.UploadAsync(provider, context.ObjectPath($"paging/{name}"), Encoding.UTF8.GetBytes(name));
        }

        var first = await provider.ListPageAsync(folder, new RemotePageRequest(100));
        GenericS3EndToEndContext.AssertSuccess(first, "List first S3 page");
        Assert.InRange(first.GetValueOrThrow().Items.Count, 1, 100);
        Assert.NotNull(first.GetValueOrThrow().NextCursor);

        var second = await provider.ListPageAsync(
            folder,
            new RemotePageRequest(100, first.GetValueOrThrow().NextCursor));
        GenericS3EndToEndContext.AssertSuccess(second, "List next S3 page");
        Assert.InRange(second.GetValueOrThrow().Items.Count, 1, 100);
        Assert.NotNull(second.GetValueOrThrow().NextCursor);

        var allItems = new List<RemoteItem>();
        allItems.AddRange(first.GetValueOrThrow().Items);
        allItems.AddRange(second.GetValueOrThrow().Items);
        var cursorAfterPage = second.GetValueOrThrow().NextCursor;
        var pageCount = 2;
        while (cursorAfterPage is not null)
        {
            var page = await provider.ListPageAsync(folder, new RemotePageRequest(100, cursorAfterPage));
            GenericS3EndToEndContext.AssertSuccess(page, "List remaining S3 page");
            allItems.AddRange(page.GetValueOrThrow().Items);
            cursorAfterPage = page.GetValueOrThrow().NextCursor;
            pageCount++;
        }

        var allNames = allItems.Select(item => item.Name).ToArray();
        Assert.True(pageCount >= 3);
        Assert.Equal(205, allNames.Distinct(StringComparer.Ordinal).Count());

        // Desktop's previous-page stack replays the cursor used for the prior request.
        var previous = await provider.ListPageAsync(folder, new RemotePageRequest(100));
        GenericS3EndToEndContext.AssertSuccess(previous, "Replay previous S3 page cursor");
        Assert.Equal(
            first.GetValueOrThrow().Items.Select(item => item.Name),
            previous.GetValueOrThrow().Items.Select(item => item.Name));

        var matches = new List<RemoteItem>();
        RemotePageCursor? cursor = null;
        do
        {
            var page = await provider.ListPageAsync(folder, new RemotePageRequest(4, cursor, "match-"));
            GenericS3EndToEndContext.AssertSuccess(page, "Search S3 page by prefix");
            matches.AddRange(page.GetValueOrThrow().Items);
            cursor = page.GetValueOrThrow().NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(11, matches.Count);
        Assert.All(matches, item => Assert.StartsWith("match-", item.Name, StringComparison.Ordinal));

        var noMatches = await provider.ListPageAsync(folder, new RemotePageRequest(10, searchPrefix: "不存在-"));
        GenericS3EndToEndContext.AssertSuccess(noMatches, "Search S3 page without matches");
        Assert.Empty(noMatches.GetValueOrThrow().Items);
    }

    private static async Task VerifyFolderRenameMoveAndContinuationAsync(
        GenericS3EndToEndContext context,
        AtomBox.Core.Providers.IStorageProvider provider)
    {
        var source = context.FolderPath("move/source");
        GenericS3EndToEndContext.AssertSuccess(await provider.CreateFolderAsync(source), "Create move source folder");
        GenericS3EndToEndContext.AssertSuccess(
            await provider.CreateFolderAsync(context.FolderPath("move/source/nested")),
            "Create nested move folder");
        await context.UploadAsync(provider, context.ObjectPath("move/source/nested/child.txt"), "nested"u8.ToArray());

        for (var index = 0; index < 1001; index++)
        {
            await context.UploadAsync(provider, context.ObjectPath($"move/source/file-{index:D4}.bin"), []);
        }

        var rename = await provider.RenameAsync(source, "renamed");
        GenericS3EndToEndContext.AssertSuccess(rename, "Rename folder containing more than 1000 objects");
        var renamed = context.FolderPath("move/renamed");
        var renamedList = await provider.ListPageAsync(renamed, new RemotePageRequest(10));
        GenericS3EndToEndContext.AssertSuccess(renamedList, "List renamed folder");
        Assert.NotEmpty(renamedList.GetValueOrThrow().Items);

        var destination = context.FolderPath("move/final");
        var move = await provider.MoveAsync(renamed, destination);
        GenericS3EndToEndContext.AssertSuccess(move, "Move renamed folder");

        var movedCount = 0;
        RemotePageCursor? cursor = null;
        do
        {
            var page = await provider.ListPageAsync(destination, new RemotePageRequest(250, cursor));
            GenericS3EndToEndContext.AssertSuccess(page, "List moved folder pages");
            movedCount += page.GetValueOrThrow().Items.Count(item => item.Kind == RemoteItemKind.File);
            cursor = page.GetValueOrThrow().NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(1001, movedCount);
        var nested = await provider.ListAsync(context.FolderPath("move/final/nested"));
        GenericS3EndToEndContext.AssertSuccess(nested, "List nested folder after move");
        Assert.Contains(nested.GetValueOrThrow(), item => item.Name == "child.txt");

        var oldFolder = await provider.ListAsync(source);
        GenericS3EndToEndContext.AssertSuccess(oldFolder, "List old folder after move");
        Assert.Empty(oldFolder.GetValueOrThrow());
    }

    private static async Task VerifyNotFoundMappingAsync(
        GenericS3EndToEndContext context,
        AtomBox.Core.Providers.IStorageProvider provider)
    {
        await using var destination = new MemoryStream();
        var result = await provider.DownloadAsync(context.ObjectPath("missing/does-not-exist.bin"), destination);
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    private static void AssertProgress(IReadOnlyList<TransferProgress> values, long expectedBytes, string direction)
    {
        Assert.NotEmpty(values);
        Assert.Equal(expectedBytes, values[^1].BytesTransferred);
        Assert.Equal(expectedBytes, values[^1].TotalBytes);
        for (var index = 1; index < values.Count; index++)
        {
            Assert.True(
                values[index].BytesTransferred >= values[index - 1].BytesTransferred,
                $"{direction} progress must be monotonic.");
        }
    }

    private sealed class RecordingProgress : IProgress<TransferProgress>
    {
        public List<TransferProgress> Values { get; } = [];

        public void Report(TransferProgress value) => Values.Add(value);
    }
}
