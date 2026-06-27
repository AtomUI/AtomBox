using AtomBox.Core.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

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
}
