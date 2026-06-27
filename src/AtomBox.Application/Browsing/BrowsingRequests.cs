using AtomBox.Core.Accounts;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Application.Browsing;

public sealed record ResolveRemoteEntryRequest(StorageProviderCategory ProviderCategory);

public sealed record ListRemoteItemsRequest(
    StorageAccountId StorageAccountId,
    RemotePath Path,
    RemotePageRequest? PageRequest = null,
    string? SearchPrefix = null);

public sealed record DeleteRemoteItemRequest(StorageAccountId StorageAccountId, RemotePath Path, RemoteItemKind Kind);

public sealed record RenameRemoteItemRequest(
    StorageAccountId StorageAccountId,
    RemotePath Path,
    RemoteItemKind Kind,
    string NewName);

public sealed record CreateRemoteFolderRequest(StorageAccountId StorageAccountId, RemotePath Path);

public sealed record GetRemotePathContextRequest(RemotePath Path);
