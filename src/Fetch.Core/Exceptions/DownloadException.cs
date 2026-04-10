namespace Fetch.Core.Exceptions;

public class DownloadException : Exception
{
    public int ChunkIndex { get; }
    public long Offset { get; }
    public long BytesWritten { get; }

    public DownloadException(string message, int chunkIndex, long offset, long bytesWritten, Exception? innerException = null)
        : base(message, innerException)
    {
        ChunkIndex = chunkIndex;
        Offset = offset;
        BytesWritten = bytesWritten;
    }
}
