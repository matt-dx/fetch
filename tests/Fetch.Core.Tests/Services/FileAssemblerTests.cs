using Fetch.Core.Configuration;
using Fetch.Core.Models;
using Fetch.Core.Services;
using FluentAssertions;

namespace Fetch.Core.Tests.Services;

public class FileAssemblerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileAssembler _assembler;

    public FileAssemblerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fetch-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = new DownloadOptions
        {
            BlobUri = new Uri("https://test.blob.core.windows.net/c/f"),
            BufferSizeBytes = 1024,
            MaxConcurrency = 4
        };
        _assembler = new FileAssembler(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task AssembleAsync_WritesChunksAtCorrectOffsets()
    {
        var chunk0Data = new byte[1024];
        var chunk1Data = new byte[1024];
        var chunk2Data = new byte[512];

        Random.Shared.NextBytes(chunk0Data);
        Random.Shared.NextBytes(chunk1Data);
        Random.Shared.NextBytes(chunk2Data);

        var totalSize = chunk0Data.Length + chunk1Data.Length + chunk2Data.Length;

        var chunks = new List<ChunkState>
        {
            CreateChunk(0, 0, chunk0Data.Length, chunk0Data),
            CreateChunk(1, chunk0Data.Length, chunk1Data.Length, chunk1Data),
            CreateChunk(2, chunk0Data.Length + chunk1Data.Length, chunk2Data.Length, chunk2Data)
        };

        var manifest = new DownloadManifest
        {
            TotalSize = totalSize,
            Chunks = chunks
        };

        var outputPath = Path.Combine(_tempDir, "output.bin");
        await _assembler.AssembleAsync(outputPath, manifest);

        var result = await File.ReadAllBytesAsync(outputPath);
        result.Length.Should().Be(totalSize);
        result.AsSpan(0, 1024).ToArray().Should().BeEquivalentTo(chunk0Data);
        result.AsSpan(1024, 1024).ToArray().Should().BeEquivalentTo(chunk1Data);
        result.AsSpan(2048, 512).ToArray().Should().BeEquivalentTo(chunk2Data);
    }

    [Fact]
    public async Task AssembleAsync_DeletesChunkTempFiles()
    {
        var data = new byte[256];
        Random.Shared.NextBytes(data);

        var chunks = new List<ChunkState>
        {
            CreateChunk(0, 0, data.Length, data)
        };

        var manifest = new DownloadManifest
        {
            TotalSize = data.Length,
            Chunks = chunks
        };

        var outputPath = Path.Combine(_tempDir, "output.bin");
        await _assembler.AssembleAsync(outputPath, manifest);

        File.Exists(chunks[0].TempFilePath).Should().BeFalse();
    }

    private ChunkState CreateChunk(int index, long offset, long length, byte[] data)
    {
        var tempPath = Path.Combine(_tempDir, $"chunk.{index:D6}");
        File.WriteAllBytes(tempPath, data);

        return new ChunkState
        {
            Index = index,
            Offset = offset,
            Length = length,
            BytesWritten = length,
            TempFilePath = tempPath
        };
    }
}
