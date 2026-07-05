namespace AtomBox.Core.Previews;

public sealed record PreviewRemoteFileResult(
    RemotePreviewKind Kind,
    string FileName,
    string ContentType,
    long Size,
    byte[] Content,
    string? Text,
    string? EncodingName);
