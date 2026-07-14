using AtomBox.Core.Previews;

namespace AtomBox.Desktop.Dialogs;

public sealed record PreviewDialogRequest(
    RemotePreviewKind Kind,
    string FileName,
    string ContentType,
    long Size,
    Func<CancellationToken, ValueTask<Stream>>? OpenImageStreamAsync,
    string? Text,
    string? EncodingName);
