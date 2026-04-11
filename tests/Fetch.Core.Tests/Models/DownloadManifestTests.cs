using System.Text.Json;
using Fetch.Core.Models;
using FluentAssertions;

namespace Fetch.Core.Tests.Models;

public class DownloadManifestTests : IDisposable
{
    private readonly string _tempDir;

    public DownloadManifestTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fetch-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void TotalBytesWritten_SumsAllChunks()
    {
        var manifest = new DownloadManifest
        {
            TotalSize = 3000,
            Chunks =
            [
                new ChunkState { Index = 0, Length = 1000, BytesWritten = 1000 },
                new ChunkState { Index = 1, Length = 1000, BytesWritten = 500 },
                new ChunkState { Index = 2, Length = 1000, BytesWritten = 0 }
            ]
        };

        manifest.TotalBytesWritten.Should().Be(1500);
    }

    [Fact]
    public void Progress_ReturnsCorrectRatio()
    {
        var manifest = new DownloadManifest
        {
            TotalSize = 2000,
            Chunks =
            [
                new ChunkState { Index = 0, Length = 1000, BytesWritten = 1000 },
                new ChunkState { Index = 1, Length = 1000, BytesWritten = 500 }
            ]
        };

        manifest.Progress.Should().BeApproximately(0.75, 0.001);
    }

    [Fact]
    public void Progress_WhenTotalSizeZero_ReturnsZero()
    {
        var manifest = new DownloadManifest { TotalSize = 0 };
        manifest.Progress.Should().Be(0);
    }

    [Fact]
    public async Task SaveAsync_And_LoadAsync_RoundTrip()
    {
        var blobUri = new Uri("https://account.blob.core.windows.net/container/file.zip");
        var hash = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

        var original = new DownloadManifest
        {
            BlobUri = blobUri,
            TotalSize = 1024 * 1024,
            ContentHash = hash,
            ChunkSizeBytes = 512 * 1024,
            Chunks =
            [
                new ChunkState { Index = 0, Offset = 0, Length = 512 * 1024, BytesWritten = 512 * 1024 },
                new ChunkState { Index = 1, Offset = 512 * 1024, Length = 512 * 1024, BytesWritten = 256 * 1024 }
            ]
        };

        var path = Path.Combine(_tempDir, "manifest.json");
        await original.SaveAsync(path);

        var loaded = await DownloadManifest.LoadAsync(path);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(original.Id);
        loaded.BlobUri.Should().Be(blobUri);
        loaded.TotalSize.Should().Be(1024 * 1024);
        loaded.ContentHash.Should().BeEquivalentTo(hash);
        loaded.ChunkSizeBytes.Should().Be(512 * 1024);
        loaded.Chunks.Should().HaveCount(2);
        loaded.Chunks[0].BytesWritten.Should().Be(512 * 1024);
        loaded.Chunks[1].BytesWritten.Should().Be(256 * 1024);
    }

    [Fact]
    public async Task LoadAsync_FileNotExists_ReturnsNull()
    {
        var result = await DownloadManifest.LoadAsync(Path.Combine(_tempDir, "nonexistent.json"));
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_CorruptJson_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "corrupt-manifest.json");
        await File.WriteAllTextAsync(path, "NOT VALID JSON {{{");

        var result = await DownloadManifest.LoadAsync(path);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "empty-manifest.json");
        await File.WriteAllTextAsync(path, "");

        var result = await DownloadManifest.LoadAsync(path);

        result.Should().BeNull();
    }

    [Fact]
    public void AssignTempFilePaths_Hidden_SetsCorrectPaths()
    {
        var manifest = new DownloadManifest
        {
            Chunks =
            [
                new ChunkState { Index = 0 },
                new ChunkState { Index = 1 },
                new ChunkState { Index = 12 }
            ]
        };

        manifest.AssignTempFilePaths("/output/file.zip", hidden: true);

        manifest.Chunks[0].TempFilePath.Should().EndWith(".file.zip.000000");
        manifest.Chunks[1].TempFilePath.Should().EndWith(".file.zip.000001");
        manifest.Chunks[2].TempFilePath.Should().EndWith(".file.zip.000012");
    }

    [Fact]
    public void AssignTempFilePaths_Visible_SetsCorrectPaths()
    {
        var manifest = new DownloadManifest
        {
            Chunks =
            [
                new ChunkState { Index = 0 },
                new ChunkState { Index = 1 },
                new ChunkState { Index = 12 }
            ]
        };

        manifest.AssignTempFilePaths("/output/file.zip", hidden: false);

        manifest.Chunks[0].TempFilePath.Should().EndWith("file.zip.000000");
        manifest.Chunks[1].TempFilePath.Should().EndWith("file.zip.000001");
        manifest.Chunks[2].TempFilePath.Should().EndWith("file.zip.000012");
        // Ensure no dot prefix
        Path.GetFileName(manifest.Chunks[0].TempFilePath).Should().StartWith("file.zip");
    }

    [Fact]
    public void GetManifestPath_Hidden_ReturnsCorrectPath()
    {
        var result = DownloadManifest.GetManifestPath("/output/file.zip", hidden: true);
        Path.GetFileName(result).Should().Be(".file.zip.fetch-manifest.json");
    }

    [Fact]
    public void GetManifestPath_Visible_ReturnsCorrectPath()
    {
        var result = DownloadManifest.GetManifestPath("/output/file.zip", hidden: false);
        Path.GetFileName(result).Should().Be("file.zip.fetch-manifest.json");
    }
}
