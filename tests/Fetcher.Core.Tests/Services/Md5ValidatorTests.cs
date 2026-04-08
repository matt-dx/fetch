using System.Security.Cryptography;
using Fetcher.Core.Services;
using FluentAssertions;

namespace Fetcher.Core.Tests.Services;

public class Md5ValidatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Md5Validator _validator;

    public Md5ValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fetcher-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _validator = new Md5Validator();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task ValidateAsync_MatchingHash_ReturnsTrue()
    {
        var data = new byte[4096];
        Random.Shared.NextBytes(data);

        var filePath = Path.Combine(_tempDir, "test.bin");
        await File.WriteAllBytesAsync(filePath, data);

        var expectedHash = MD5.HashData(data);

        var result = await _validator.ValidateAsync(filePath, expectedHash);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_MismatchingHash_ReturnsFalse()
    {
        var data = new byte[4096];
        Random.Shared.NextBytes(data);

        var filePath = Path.Combine(_tempDir, "test.bin");
        await File.WriteAllBytesAsync(filePath, data);

        var wrongHash = new byte[16];

        var result = await _validator.ValidateAsync(filePath, wrongHash);
        result.Should().BeFalse();
    }
}
