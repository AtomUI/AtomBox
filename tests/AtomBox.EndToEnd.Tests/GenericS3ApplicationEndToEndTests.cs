using AtomBox.Application.Accounts;
using AtomBox.Application.Browsing;
using AtomBox.Core.Errors;
using AtomBox.Core.Previews;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.ValueObjects;
using System.Text;

namespace AtomBox.EndToEnd.Tests;

public sealed class GenericS3ApplicationEndToEndTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    [Trait("Category", "ManualIntegration")]
    public async Task Application_BrowsingPreviewRenameSearchAndRecursiveDelete_WorkAgainstMinio()
    {
        await using var context = await GenericS3EndToEndContext.CreateAsync();
        if (context is null)
        {
            return;
        }

        var accountService = context.GetRequiredService<AccountAppService>();
        var browser = context.GetRequiredService<RemoteBrowserAppService>();
        var connection = await accountService.TestConnectionAsync(new TestConnectionRequest(context.Account.Id));
        GenericS3EndToEndContext.AssertSuccess(connection, "Test Generic S3 connection through Application");

        var createFolder = await browser.CreateRemoteFolderAsync(new CreateRemoteFolderRequest(
            context.Account.Id,
            context.FolderPath("application")));
        GenericS3EndToEndContext.AssertSuccess(createFolder, "Create folder through Application");

        var text = "AtomBox 文本预览\nsecond line\n";
        var textPath = context.ObjectPath("application/preview-中文.txt");
        var imagePath = context.ObjectPath("application/preview.png");
        await using (var provider = await context.CreateProviderAsync())
        {
            await context.UploadAsync(provider, textPath, Encoding.UTF8.GetBytes(text));
            await context.UploadAsync(provider, imagePath, OnePixelPng);
            await context.UploadAsync(provider, context.ObjectPath("application/search-alpha.txt"), "alpha"u8.ToArray());
            await context.UploadAsync(provider, context.ObjectPath("application/search-beta.txt"), "beta"u8.ToArray());
            await context.UploadAsync(provider, context.ObjectPath("application/中文搜索.txt"), "中文"u8.ToArray());
        }

        var list = await browser.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            context.Account.Id,
            context.FolderPath("application"),
            new RemotePageRequest(100)));
        GenericS3EndToEndContext.AssertSuccess(list, "List folder through Application");
        Assert.Contains(list.GetValueOrThrow().Items, item => item.Name == "preview-中文.txt");

        var search = await browser.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            context.Account.Id,
            context.FolderPath("application"),
            new RemotePageRequest(10),
            "search-a"));
        GenericS3EndToEndContext.AssertSuccess(search, "Search through Application");
        Assert.Single(search.GetValueOrThrow().Items);
        Assert.Equal("search-alpha.txt", search.GetValueOrThrow().Items[0].Name);

        var chineseSearch = await browser.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            context.Account.Id,
            context.FolderPath("application"),
            new RemotePageRequest(10),
            "中文"));
        GenericS3EndToEndContext.AssertSuccess(chineseSearch, "Search Chinese prefix through Application");
        Assert.Single(chineseSearch.GetValueOrThrow().Items);
        Assert.Equal("中文搜索.txt", chineseSearch.GetValueOrThrow().Items[0].Name);

        var textPreview = await browser.PreviewRemoteFileAsync(new PreviewRemoteFileRequest(
            context.Account.Id,
            textPath,
            "preview-中文.txt",
            Encoding.UTF8.GetByteCount(text),
            RemoteItemKind.File));
        GenericS3EndToEndContext.AssertSuccess(textPreview, "Preview text through Application");
        Assert.Equal(RemotePreviewKind.Text, textPreview.GetValueOrThrow().Kind);
        Assert.Equal(text, textPreview.GetValueOrThrow().Text);
        Assert.Equal("utf-8", textPreview.GetValueOrThrow().EncodingName);

        var imageMetadata = await browser.PreviewRemoteFileAsync(new PreviewRemoteFileRequest(
            context.Account.Id,
            imagePath,
            "preview.png",
            OnePixelPng.LongLength,
            RemoteItemKind.File));
        GenericS3EndToEndContext.AssertSuccess(imageMetadata, "Resolve image preview metadata");
        Assert.Equal(RemotePreviewKind.Image, imageMetadata.GetValueOrThrow().Kind);

        var imagePreview = await browser.OpenRemoteImagePreviewStreamAsync(new PreviewRemoteFileRequest(
            context.Account.Id,
            imagePath,
            "preview.png",
            OnePixelPng.LongLength,
            RemoteItemKind.File));
        GenericS3EndToEndContext.AssertSuccess(imagePreview, "Open image preview stream through Application");
        await using (var stream = imagePreview.GetValueOrThrow().Content)
        await using (var destination = new MemoryStream())
        {
            await stream.CopyToAsync(destination);
            Assert.Equal(OnePixelPng, destination.ToArray());
        }

        var tooLarge = await browser.PreviewRemoteFileAsync(
            new PreviewRemoteFileRequest(
                context.Account.Id,
                textPath,
                "preview-中文.txt",
                RemotePreviewOptions.DefaultMaxTextBytes + 1,
                RemoteItemKind.File));
        Assert.True(tooLarge.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, tooLarge.Error?.Category);

        var rename = await browser.RenameRemoteItemAsync(new RenameRemoteItemRequest(
            context.Account.Id,
            textPath,
            RemoteItemKind.File,
            "renamed-中文.txt"));
        GenericS3EndToEndContext.AssertSuccess(rename, "Rename file through Application");

        await VerifyRecursiveDeleteAcrossPagesAsync(context, browser);

        await context.WriteEvidenceManifestAsync(new Dictionary<string, object?>
        {
            ["applicationBrowsing"] = "passed",
            ["textPreview"] = "passed",
            ["imagePreviewStream"] = "passed",
            ["recursiveDeleteObjectCount"] = 205
        });
    }

    private static async Task VerifyRecursiveDeleteAcrossPagesAsync(
        GenericS3EndToEndContext context,
        RemoteBrowserAppService browser)
    {
        var deleteFolder = context.FolderPath("delete-cases");
        GenericS3EndToEndContext.AssertSuccess(
            await browser.CreateRemoteFolderAsync(new CreateRemoteFolderRequest(context.Account.Id, deleteFolder)),
            "Create recursive delete folder");
        GenericS3EndToEndContext.AssertSuccess(
            await browser.CreateRemoteFolderAsync(new CreateRemoteFolderRequest(
                context.Account.Id,
                context.FolderPath("delete-cases/nested"))),
            "Create recursive delete nested folder");

        await using (var provider = await context.CreateProviderAsync())
        {
            for (var index = 0; index < 205; index++)
            {
                var relative = index % 2 == 0
                    ? $"delete-cases/item-{index:D4}.txt"
                    : $"delete-cases/nested/item-{index:D4}.txt";
                await context.UploadAsync(provider, context.ObjectPath(relative), [(byte)(index % 251)]);
            }
        }

        var delete = await browser.DeleteRemoteItemAsync(new DeleteRemoteItemRequest(
            context.Account.Id,
            deleteFolder,
            RemoteItemKind.Folder));
        GenericS3EndToEndContext.AssertSuccess(delete, "Recursively delete folder through Application");

        var verify = await browser.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            context.Account.Id,
            context.RunFolderPath,
            new RemotePageRequest(100)));
        GenericS3EndToEndContext.AssertSuccess(verify, "Verify recursive folder deletion");
        Assert.DoesNotContain(verify.GetValueOrThrow().Items, item => item.Name == "delete-cases");
    }
}
