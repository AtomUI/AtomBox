namespace AtomBox.Core.Previews;

public sealed record RemotePreviewStreamResult(
    Stream Content,
    long Size);
