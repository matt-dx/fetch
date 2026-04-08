namespace Fetcher.Core.Services;

public interface IIntegrityValidator
{
    Task<bool> ValidateAsync(string filePath, byte[] expectedHash, CancellationToken ct = default);
}
