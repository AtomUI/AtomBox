using AtomBox.Core.RemoteItems;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Previews;

public sealed record PreviewRemoteFileRequest(
    StorageAccountId StorageAccountId,
    RemotePath Path,
    string FileName,
    long? Size,
    RemoteItemKind Kind);
