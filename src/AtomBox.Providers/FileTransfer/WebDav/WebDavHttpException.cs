namespace AtomBox.Providers.FileTransfer.WebDav;

internal sealed class WebDavHttpException : Exception
{
    public WebDavHttpException(int statusCode, string reasonPhrase)
        : base("WebDAV request failed.")
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
    }

    public int StatusCode { get; }

    public string ReasonPhrase { get; }
}
