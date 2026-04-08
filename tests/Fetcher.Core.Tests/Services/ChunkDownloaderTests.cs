using Fetcher.Core.Configuration;
using Fetcher.Core.Exceptions;
using Fetcher.Core.Models;
using Fetcher.Core.Orchestration;
using Fetcher.Core.Services;
using FluentAssertions;
using NSubstitute;

namespace Fetcher.Core.Tests.Services;

public class ChunkDownloaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DownloadOptions _options;
    private readonly IBlobService _blobService;
    private readonly IDownloadProgress _progress;
    private readonly ChunkDownloader _downloader;

    public ChunkDownloaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fetcher-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _options = new DownloadOptions
        {
            BlobUri = new Uri("https://test.blob.core.windows.net/c/f"),
            BufferSizeBytes = 1024,
            MaxRetriesPerChunk = 3,
            RetryBaseDelay = TimeSpan.FromMilliseconds(10)
        };

        _blobService = Substitute.For<IBlobService>();
        _progress = Substitute.For<IDownloadProgress>();
        _downloader = new ChunkDownloader(_options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task DownloadChunkAsync_HappyPath_WritesCorrectBytes()
    {
        var data = new byte[2048];
        Random.Shared.NextBytes(data);

        var chunk = CreateChunk(0, 0, data.Length);

        _blobService.OpenChunkStreamAsync(0, data.Length, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(data));

        await _downloader.DownloadChunkAsync(chunk, _blobService, _progress);

        var written = await File.ReadAllBytesAsync(chunk.TempFilePath);
        written.Should().BeEquivalentTo(data);
        chunk.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task DownloadChunkAsync_AlreadyComplete_DoesNothing()
    {
        var chunk = CreateChunk(0, 0, 1000);
        chunk.BytesWritten = 1000;

        await _downloader.DownloadChunkAsync(chunk, _blobService, _progress);

        await _blobService.DidNotReceive().OpenChunkStreamAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadChunkAsync_Resume_RequestsRemainingBytes()
    {
        var fullData = new byte[2048];
        Random.Shared.NextBytes(fullData);

        var chunk = CreateChunk(0, 0, fullData.Length);

        // Write first half to simulate partial download
        var firstHalf = fullData.AsMemory(0, 1024);
        await File.WriteAllBytesAsync(chunk.TempFilePath, firstHalf.ToArray());
        chunk.BytesWritten = 1024;

        var secondHalf = fullData.AsMemory(1024).ToArray();
        _blobService.OpenChunkStreamAsync(1024, 1024, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(secondHalf));

        await _downloader.DownloadChunkAsync(chunk, _blobService, _progress);

        var written = await File.ReadAllBytesAsync(chunk.TempFilePath);
        written.Should().BeEquivalentTo(fullData);
        chunk.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task DownloadChunkAsync_TransientFailureThenSuccess_Retries()
    {
        var data = new byte[512];
        Random.Shared.NextBytes(data);

        var chunk = CreateChunk(0, 0, data.Length);

        var callCount = 0;
        _blobService.OpenChunkStreamAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1)
                    throw new IOException("Transient failure");
                return new MemoryStream(data);
            });

        await _downloader.DownloadChunkAsync(chunk, _blobService, _progress);

        callCount.Should().Be(2);
        _progress.Received(1).ReportError(0, Arg.Any<IOException>(), 1);
    }

    [Fact]
    public async Task DownloadChunkAsync_MaxRetriesExceeded_ThrowsDownloadException()
    {
        var chunk = CreateChunk(0, 0, 1000);

        _blobService.OpenChunkStreamAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns<Stream>(_ => throw new IOException("Persistent failure"));

        var act = () => _downloader.DownloadChunkAsync(chunk, _blobService, _progress);

        await act.Should().ThrowAsync<DownloadException>()
            .Where(ex => ex.ChunkIndex == 0);
    }

    [Fact]
    public async Task DownloadChunkAsync_Cancellation_ThrowsOperationCanceled()
    {
        var chunk = CreateChunk(0, 0, 1000);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _blobService.OpenChunkStreamAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns<Stream>(_ => throw new OperationCanceledException());

        var act = () => _downloader.DownloadChunkAsync(chunk, _blobService, _progress, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private ChunkState CreateChunk(int index, long offset, long length)
    {
        return new ChunkState
        {
            Index = index,
            Offset = offset,
            Length = length,
            TempFilePath = Path.Combine(_tempDir, $"chunk.{index:D6}")
        };
    }
}
