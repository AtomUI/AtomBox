using AtomBox.Core.Previews;

namespace AtomBox.Desktop.Dialogs;

public sealed record PreviewDialogRequest(
    RemotePreviewKind Kind,
    string FileName,
    string ContentType,
    long Size,
    byte[] Content,
    string? Text,
    string? EncodingName);
