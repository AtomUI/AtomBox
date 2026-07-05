using AtomBox.Core.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.Previews;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using System.Text;

namespace AtomBox.Application.Browsing;

public sealed class RemoteBrowserAppService
{
    private readonly IStorageAccountRepository _accounts;
    private readonly IStorageProviderFactory _providerFactory;

    public RemoteBrowserAppService(
        IStorageAccountRepository accounts,
        IStorageProviderFactory providerFactory)
    {
        _accounts = accounts;
        _providerFactory = providerFactory;
    }

    public async Task<OperationResult<RemoteEntryResult>> ResolveEntryAsync(
        ResolveRemoteEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        var accountsResult = await _accounts.ListAsync(cancellationToken).ConfigureAwait(false);
        if (accountsResult.IsFailure)
        {
            return OperationResult<RemoteEntryResult>.Failure(accountsResult.Error!);
        }

        var accounts = accountsResult.GetValueOrThrow()
            .Where(account => account.ProviderCategory == request.ProviderCategory)
            .ToArray();

        var state = accounts.Length switch
        {
            0 => RemoteEntryState.Empty,
            1 => RemoteEntryState.SingleAccount,
            _ => RemoteEntryState.AccountSelection
        };

        return OperationResult<RemoteEntryResult>.Success(new RemoteEntryResult(
            state,
            accounts.Select(account => account.Id).ToArray()));
    }

    public async Task<OperationResult<ListRemoteItemsResult>> ListRemoteItemsAsync(
        ListRemoteItemsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.StorageAccountId.IsEmpty)
        {
            return OperationResult<ListRemoteItemsResult>.Failure(StorageError.Validation("Storage account id is required."));
        }

        var accountResult = await _accounts.GetByIdAsync(request.StorageAccountId, cancellationToken).ConfigureAwait(false);
        if (accountResult.IsFailure)
        {
            return OperationResult<ListRemoteItemsResult>.Failure(accountResult.Error!);
        }

        var providerResult = await _providerFactory.CreateAsync(accountResult.GetValueOrThrow(), cancellationToken).ConfigureAwait(false);
        if (providerResult.IsFailure)
        {
            return OperationResult<ListRemoteItemsResult>.Failure(providerResult.Error!);
        }

        await using var provider = providerResult.GetValueOrThrow();
        var pageRequest = request.PageRequest ?? RemotePageRequest.FirstPage;
        var searchPrefix = string.IsNullOrWhiteSpace(request.SearchPrefix)
            ? pageRequest.SearchPrefix
            : request.SearchPrefix;
        var listResult = await provider.ListPageAsync(
            request.Path,
            new RemotePageRequest(pageRequest.PageSize, pageRequest.Cursor, searchPrefix),
            cancellationToken).ConfigureAwait(false);
        if (listResult.IsFailure)
        {
            return OperationResult<ListRemoteItemsResult>.Failure(listResult.Error!);
        }

        var page = listResult.GetValueOrThrow();
        return OperationResult<ListRemoteItemsResult>.Success(new ListRemoteItemsResult(
            page.Path,
            page.Items,
            page.PreviousCursor,
            page.NextCursor,
            page.PageSize,
            RemotePathContextResult.From(page.Path, page.PreviousCursor, page.NextCursor),
            request));
    }

    public async Task<OperationResult<PreviewRemoteFileResult>> PreviewRemoteFileAsync(
        PreviewRemoteFileRequest request,
        RemotePreviewOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= RemotePreviewOptions.Default;

        if (request.StorageAccountId.IsEmpty)
        {
            return OperationResult<PreviewRemoteFileResult>.Failure(StorageError.Validation("Storage account id is required."));
        }

        if (request.Kind != RemoteItemKind.File)
        {
            return OperationResult<PreviewRemoteFileResult>.Failure(StorageError.NotSupported("Only remote files can be previewed."));
        }

        var fileName = request.FileName.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return OperationResult<PreviewRemoteFileResult>.Failure(StorageError.Validation("Remote file name is required."));
        }

        if (request.Size < 0)
        {
            return OperationResult<PreviewRemoteFileResult>.Failure(StorageError.Validation("Remote file size cannot be negative."));
        }

        if (!TryResolvePreviewType(fileName, out var previewKind, out var contentType))
        {
            return OperationResult<PreviewRemoteFileResult>.Failure(StorageError.NotSupported("This file type cannot be previewed. Please download it to view."));
        }

        var maxBytes = previewKind == RemotePreviewKind.Text ? options.MaxTextBytes : options.MaxImageBytes;
        if (request.Size is > 0 && request.Size > maxBytes)
        {
            return OperationResult<PreviewRemoteFileResult>.Failure(StorageError.NotSupported("This file is too large to preview. Please download it to view."));
        }

        var accountResult = await _accounts.GetByIdAsync(request.StorageAccountId, cancellationToken).ConfigureAwait(false);
        if (accountResult.IsFailure)
        {
            return OperationResult<PreviewRemoteFileResult>.Failure(accountResult.Error!);
        }

        var providerResult = await _providerFactory.CreateAsync(accountResult.GetValueOrThrow(), cancellationToken).ConfigureAwait(false);
        if (providerResult.IsFailure)
        {
            return OperationResult<PreviewRemoteFileResult>.Failure(providerResult.Error!);
        }

        await using var provider = providerResult.GetValueOrThrow();
        await using var destination = new LimitedMemoryStream(maxBytes);
        OperationResult downloadResult;
        try
        {
            downloadResult = await provider.DownloadAsync(request.Path, destination, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("preview content exceeded", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<PreviewRemoteFileResult>.Failure(StorageError.NotSupported("This file is too large to preview. Please download it to view."));
        }
        if (downloadResult.IsFailure)
        {
            return OperationResult<PreviewRemoteFileResult>.Failure(downloadResult.Error!);
        }

        var content = destination.ToArray();
        if (content.LongLength > maxBytes)
        {
            return OperationResult<PreviewRemoteFileResult>.Failure(StorageError.NotSupported("This file is too large to preview. Please download it to view."));
        }

        if (previewKind == RemotePreviewKind.Image)
        {
            return OperationResult<PreviewRemoteFileResult>.Success(new PreviewRemoteFileResult(
                RemotePreviewKind.Image,
                fileName,
                contentType,
                content.LongLength,
                content,
                Text: null,
                EncodingName: null));
        }

        var textResult = TryDecodeText(content);
        if (textResult is null)
        {
            return OperationResult<PreviewRemoteFileResult>.Failure(StorageError.NotSupported("This text file encoding cannot be previewed. Please download it to view."));
        }

        return OperationResult<PreviewRemoteFileResult>.Success(new PreviewRemoteFileResult(
            RemotePreviewKind.Text,
            fileName,
            contentType,
            content.LongLength,
            content,
            textResult.Value.Text,
            textResult.Value.EncodingName));
    }

    public async Task<OperationResult> DeleteRemoteItemAsync(
        DeleteRemoteItemRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.StorageAccountId.IsEmpty)
        {
            return OperationResult.Failure(StorageError.Validation("Storage account id is required."));
        }

        if (request.Kind is not (RemoteItemKind.File or RemoteItemKind.Folder))
        {
            return OperationResult.Failure(StorageError.Validation("Only remote files and folders can be deleted."));
        }

        var accountResult = await _accounts.GetByIdAsync(request.StorageAccountId, cancellationToken).ConfigureAwait(false);
        if (accountResult.IsFailure)
        {
            return OperationResult.Failure(accountResult.Error!);
        }

        var providerResult = await _providerFactory.CreateAsync(accountResult.GetValueOrThrow(), cancellationToken).ConfigureAwait(false);
        if (providerResult.IsFailure)
        {
            return OperationResult.Failure(providerResult.Error!);
        }

        await using var provider = providerResult.GetValueOrThrow();
        return request.Kind == RemoteItemKind.Folder
            ? await DeleteFolderRecursivelyAsync(provider, request.Path, cancellationToken).ConfigureAwait(false)
            : await provider.DeleteAsync(request.Path, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<OperationResult> DeleteFolderRecursivelyAsync(
        IStorageProvider provider,
        RemotePath path,
        CancellationToken cancellationToken)
    {
        if (path.IsRoot)
        {
            return OperationResult.Failure(StorageError.Validation("Root path cannot be deleted."));
        }

        var listResult = await ListAllChildrenAsync(provider, path, cancellationToken).ConfigureAwait(false);
        if (listResult.IsFailure)
        {
            return OperationResult.Failure(listResult.Error!);
        }

        foreach (var item in listResult.GetValueOrThrow())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deleteResult = item.Kind == RemoteItemKind.Folder
                ? await DeleteFolderRecursivelyAsync(provider, item.Path, cancellationToken).ConfigureAwait(false)
                : await provider.DeleteAsync(item.Path, cancellationToken).ConfigureAwait(false);
            if (deleteResult.IsFailure)
            {
                return deleteResult;
            }
        }

        return await provider.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAllChildrenAsync(
        IStorageProvider provider,
        RemotePath path,
        CancellationToken cancellationToken)
    {
        var items = new List<RemoteItem>();
        RemotePageCursor? cursor = null;
        do
        {
            var pageResult = await provider.ListPageAsync(
                path,
                new RemotePageRequest(100, cursor),
                cancellationToken).ConfigureAwait(false);
            if (pageResult.IsFailure)
            {
                return OperationResult<IReadOnlyList<RemoteItem>>.Failure(pageResult.Error!);
            }

            var page = pageResult.GetValueOrThrow();
            items.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return OperationResult<IReadOnlyList<RemoteItem>>.Success(items);
    }

    public async Task<OperationResult> RenameRemoteItemAsync(
        RenameRemoteItemRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.StorageAccountId.IsEmpty)
        {
            return OperationResult.Failure(StorageError.Validation("Storage account id is required."));
        }

        if (request.Kind is not (RemoteItemKind.File or RemoteItemKind.Folder))
        {
            return OperationResult.Failure(StorageError.Validation("Only remote files and folders can be renamed."));
        }

        var newName = request.NewName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            return OperationResult.Failure(StorageError.Validation("New remote name is required."));
        }

        var accountResult = await _accounts.GetByIdAsync(request.StorageAccountId, cancellationToken).ConfigureAwait(false);
        if (accountResult.IsFailure)
        {
            return OperationResult.Failure(accountResult.Error!);
        }

        var providerResult = await _providerFactory.CreateAsync(accountResult.GetValueOrThrow(), cancellationToken).ConfigureAwait(false);
        if (providerResult.IsFailure)
        {
            return OperationResult.Failure(providerResult.Error!);
        }

        await using var provider = providerResult.GetValueOrThrow();
        return await provider.RenameAsync(request.Path, newName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> CreateRemoteFolderAsync(
        CreateRemoteFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.StorageAccountId.IsEmpty)
        {
            return OperationResult.Failure(StorageError.Validation("Storage account id is required."));
        }

        if (request.Path.IsRoot)
        {
            return OperationResult.Failure(StorageError.Validation("Remote folder path is required."));
        }

        var accountResult = await _accounts.GetByIdAsync(request.StorageAccountId, cancellationToken).ConfigureAwait(false);
        if (accountResult.IsFailure)
        {
            return OperationResult.Failure(accountResult.Error!);
        }

        var providerResult = await _providerFactory.CreateAsync(accountResult.GetValueOrThrow(), cancellationToken).ConfigureAwait(false);
        if (providerResult.IsFailure)
        {
            return OperationResult.Failure(providerResult.Error!);
        }

        await using var provider = providerResult.GetValueOrThrow();
        return await provider.CreateFolderAsync(request.Path, cancellationToken).ConfigureAwait(false);
    }

    public OperationResult<RemotePathContextResult> GetPathContext(GetRemotePathContextRequest request)
    {
        return OperationResult<RemotePathContextResult>.Success(RemotePathContextResult.From(request.Path));
    }

    private static bool TryResolvePreviewType(
        string fileName,
        out RemotePreviewKind kind,
        out string contentType)
    {
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".png":
                kind = RemotePreviewKind.Image;
                contentType = "image/png";
                return true;
            case ".jpg":
            case ".jpeg":
                kind = RemotePreviewKind.Image;
                contentType = "image/jpeg";
                return true;
            case ".bmp":
                kind = RemotePreviewKind.Image;
                contentType = "image/bmp";
                return true;
            case ".gif":
                kind = RemotePreviewKind.Image;
                contentType = "image/gif";
                return true;
            case ".webp":
                kind = RemotePreviewKind.Image;
                contentType = "image/webp";
                return true;
            case ".json":
                kind = RemotePreviewKind.Text;
                contentType = "application/json";
                return true;
            case ".xml":
                kind = RemotePreviewKind.Text;
                contentType = "application/xml";
                return true;
            case ".html":
                kind = RemotePreviewKind.Text;
                contentType = "text/html";
                return true;
            case ".css":
                kind = RemotePreviewKind.Text;
                contentType = "text/css";
                return true;
            case ".csv":
                kind = RemotePreviewKind.Text;
                contentType = "text/csv";
                return true;
            case ".txt":
            case ".log":
            case ".md":
            case ".markdown":
            case ".yaml":
            case ".yml":
            case ".ini":
            case ".conf":
            case ".config":
            case ".js":
            case ".ts":
            case ".cs":
            case ".py":
            case ".java":
            case ".go":
            case ".rs":
            case ".cpp":
            case ".c":
            case ".h":
            case ".sql":
            case ".sh":
            case ".ps1":
                kind = RemotePreviewKind.Text;
                contentType = "text/plain";
                return true;
            default:
                kind = default;
                contentType = string.Empty;
                return false;
        }
    }

    private static (string Text, string EncodingName)? TryDecodeText(byte[] content)
    {
        try
        {
            if (content.Length >= 3 &&
                content[0] == 0xEF &&
                content[1] == 0xBB &&
                content[2] == 0xBF)
            {
                return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true).GetString(content, 3, content.Length - 3), "utf-8");
            }

            if (content.Length >= 2 &&
                content[0] == 0xFF &&
                content[1] == 0xFE)
            {
                return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true).GetString(content, 2, content.Length - 2), "utf-16le");
            }

            if (content.Length >= 2 &&
                content[0] == 0xFE &&
                content[1] == 0xFF)
            {
                return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true).GetString(content, 2, content.Length - 2), "utf-16be");
            }

            if (LooksBinary(content))
            {
                return null;
            }

            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content), "utf-8");
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool LooksBinary(byte[] content)
    {
        foreach (var value in content)
        {
            if (value == 0)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class LimitedMemoryStream : MemoryStream
    {
        private readonly long _maxBytes;

        public LimitedMemoryStream(long maxBytes)
        {
            _maxBytes = maxBytes;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCanWrite(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCanWrite(buffer.Length);
            base.Write(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            EnsureCanWrite(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            EnsureCanWrite(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        private void EnsureCanWrite(int count)
        {
            if (Length + count > _maxBytes)
            {
                throw new InvalidOperationException("Remote preview content exceeded the configured limit.");
            }
        }
    }
}
