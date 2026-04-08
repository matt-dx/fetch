namespace Fetcher.Core.Exceptions;

public class BlobNotFoundException : Exception
{
    public Uri BlobUri { get; }

    public BlobNotFoundException(Uri blobUri, Exception? innerException = null)
        : base($"Blob not found: {blobUri}", innerException)
    {
        BlobUri = blobUri;
    }
}
