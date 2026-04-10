using System.Security.Cryptography;

namespace Fetch.Core.Services;

public sealed class Md5Validator : IIntegrityValidator
{
    public async Task<bool> ValidateAsync(string filePath, byte[] expectedHash, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var actualHash = await MD5.HashDataAsync(stream, ct);
        return actualHash.AsSpan().SequenceEqual(expectedHash);
    }
}
