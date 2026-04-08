namespace Fetcher.Core.Exceptions;

public class IntegrityException : Exception
{
    public byte[] ExpectedHash { get; }
    public byte[] ActualHash { get; }

    public IntegrityException(byte[] expectedHash, byte[] actualHash)
        : base($"Integrity check failed. Expected: {Convert.ToHexString(expectedHash)}, Actual: {Convert.ToHexString(actualHash)}")
    {
        ExpectedHash = expectedHash;
        ActualHash = actualHash;
    }
}
