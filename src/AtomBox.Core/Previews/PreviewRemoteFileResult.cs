namespace AtomBox.Core.Previews;

public sealed record PreviewRemoteFileResult(
    RemotePreviewKind Kind,
    string FileName,
    string ContentType,
    long Size,
    string? Text,
    string? EncodingName);
