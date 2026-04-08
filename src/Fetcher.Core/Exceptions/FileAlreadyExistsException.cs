namespace Fetcher.Core.Exceptions;

public class FileAlreadyExistsException : Exception
{
    public string FilePath { get; }
    public long FileSize { get; }

    public FileAlreadyExistsException(string filePath, long fileSize)
        : base($"File already exists: {filePath} ({fileSize} bytes)")
    {
        FilePath = filePath;
        FileSize = fileSize;
    }
}
