namespace AtomBox.Providers.ObjectStorage.QiniuKodo;

internal sealed class QiniuKodoHttpException : Exception
{
    public QiniuKodoHttpException(int statusCode, string? reasonPhrase)
        : base("Qiniu Kodo download request failed.")
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
    }

    public int StatusCode { get; }

    public string? ReasonPhrase { get; }
}
