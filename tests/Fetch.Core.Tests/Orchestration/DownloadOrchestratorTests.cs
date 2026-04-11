using Fetch.Core.Configuration;
using Fetch.Core.Exceptions;
using Fetch.Core.Models;
using Fetch.Core.Orchestration;
using Fetch.Core.Services;
using FluentAssertions;
using NSubstitute;

namespace Fetch.Core.Tests.Orchestration;

public class DownloadOrchestratorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IBlobService _blobService;
    private readonly IChunkDownloader _chunkDownloader;
    private readonly IFileAssembler _fileAssembler;
    private readonly IAssemblySession _assemblySession;
    private readonly IIntegrityValidator _validator;
    private readonly IDownloadProgress _progress;
    private readonly byte[] _contentHash = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

    public DownloadOrchestratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fetch-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _blobService = Substitute.For<IBlobService>();
        _chunkDownloader = Substitute.For<IChunkDownloader>();
        _fileAssembler = Substitute.For<IFileAssembler>();
        _assemblySession = Substitute.For<IAssemblySession>();
        _validator = Substitute.For<IIntegrityValidator>();
        _progress = Substitute.For<IDownloadProgress>();

        // Default: streaming assembly returns a mock session and creates the .part file
        _fileAssembler.BeginAssembly(Arg.Any<string>(), Arg.Any<long>())
            .Returns(callInfo =>
            {
                var path = callInfo.ArgAt<string>(0);
                File.Create(path).Dispose();
                return _assemblySession;
            });

        // Default: batch assembly creates the .part file
        _fileAssembler.AssembleAsync(Arg.Any<string>(), Arg.Any<DownloadManifest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var path = callInfo.ArgAt<string>(0);
                File.Create(path).Dispose();
                return Task.CompletedTask;
            });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private DownloadOrchestrator CreateOrchestrator(DownloadOptions? options = null)
    {
        options ??= new DownloadOptions
        {
            BlobUri = new Uri("https://account.blob.core.windows.net/container/file.zip"),
            LocalPath = _tempDir,
            MaxChunkSizeBytes = 512,
            MaxConcurrency = 2
        };

        return new DownloadOrchestrator(_blobService, _chunkDownloader, _fileAssembler, _validator, options);
    }

    [Fact]
    public async Task DownloadAsync_HappyPath_ReturnsSuccess()
    {
        _blobService.GetBlobMetadataAsync(Arg.Any<CancellationToken>())
            .Returns(new BlobMetadata(1024, _contentHash));

        _validator.ValidateAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.DownloadAsync(_progress);

        result.Success.Should().BeTrue();
        result.TotalBytes.Should().Be(1024);
        result.OutputPath.Should().EndWith("file.zip");

        await _chunkDownloader.Received().DownloadChunkAsync(
            Arg.Any<ChunkState>(), _blobService, _progress, Arg.Any<CancellationToken>());

        // Default mode uses streaming assembly via BeginAssembly
        _fileAssembler.Received().BeginAssembly(
            Arg.Any<string>(), 1024);
    }

    [Fact]
    public async Task DownloadAsync_WaitForDownload_UsesBatchAssembly()
    {
        _blobService.GetBlobMetadataAsync(Arg.Any<CancellationToken>())
            .Returns(new BlobMetadata(1024, _contentHash));

        _validator.ValidateAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var options = new DownloadOptions
        {
            BlobUri = new Uri("https://account.blob.core.windows.net/container/file.zip"),
            LocalPath = _tempDir,
            MaxChunkSizeBytes = 512,
            MaxConcurrency = 2,
            WaitForDownload = true
        };

        var orchestrator = CreateOrchestrator(options);
        var result = await orchestrator.DownloadAsync(_progress);

        result.Success.Should().BeTrue();

        // WaitForDownload uses batch AssembleAsync
        await _fileAssembler.Received().AssembleAsync(
            Arg.Any<string>(), Arg.Any<DownloadManifest>(), Arg.Any<CancellationToken>());

        // And reports Assembling phase separately
        Received.InOrder(() =>
        {
            _progress.ReportPhaseChanged(DownloadPhase.Downloading);
            _progress.ReportPhaseChanged(DownloadPhase.Assembling);
            _progress.ReportPhaseChanged(DownloadPhase.Validating);
        });
    }

    [Fact]
    public async Task DownloadAsync_CreatesCorrectNumberOfChunks()
    {
        _blobService.GetBlobMetadataAsync(Arg.Any<CancellationToken>())
            .Returns(new BlobMetadata(1024, _contentHash));

        _validator.ValidateAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var orchestrator = CreateOrchestrator();
        await orchestrator.DownloadAsync(_progress);

        // 1024 bytes / 512 chunk size = 2 chunks
        await _chunkDownloader.Received(2).DownloadChunkAsync(
            Arg.Any<ChunkState>(), _blobService, _progress, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadAsync_FileAlreadyExists_ThrowsFileAlreadyExistsException()
    {
        var outputPath = Path.Combine(_tempDir, "file.zip");
        var data = new byte[1024];
        await File.WriteAllBytesAsync(outputPath, data);

        _blobService.GetBlobMetadataAsync(Arg.Any<CancellationToken>())
            .Returns(new BlobMetadata(1024, _contentHash));

        var orchestrator = CreateOrchestrator();
        var act = () => orchestrator.DownloadAsync(_progress);

        await act.Should().ThrowAsync<FileAlreadyExistsException>();
    }

    [Fact]
    public async Task DownloadAsync_ChunkFailure_ReturnsFailureWithError()
    {
        _blobService.GetBlobMetadataAsync(Arg.Any<CancellationToken>())
            .Returns(new BlobMetadata(512, _contentHash));

        _chunkDownloader
            .When(x => x.DownloadChunkAsync(
                Arg.Any<ChunkState>(), Arg.Any<IBlobService>(),
                Arg.Any<IDownloadProgress>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new DownloadException("Test failure", 0, 0, 0));

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.DownloadAsync(_progress);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();

        _progress.Received().ReportPhaseChanged(DownloadPhase.Failed);
    }

    [Fact]
    public async Task DownloadAsync_NoContentHash_SkipsValidation()
    {
        _blobService.GetBlobMetadataAsync(Arg.Any<CancellationToken>())
            .Returns(new BlobMetadata(512, []));

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.DownloadAsync(_progress);

        result.Success.Should().BeTrue();

        await _validator.DidNotReceive().ValidateAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadAsync_StreamingMode_ReportsPhaseChanges()
    {
        _blobService.GetBlobMetadataAsync(Arg.Any<CancellationToken>())
            .Returns(new BlobMetadata(512, _contentHash));

        _validator.ValidateAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var orchestrator = CreateOrchestrator();
        await orchestrator.DownloadAsync(_progress);

        // Streaming mode: no separate Assembling phase
        Received.InOrder(() =>
        {
            _progress.ReportPhaseChanged(DownloadPhase.Preparing);
            _progress.ReportPhaseChanged(DownloadPhase.Downloading);
            _progress.ReportPhaseChanged(DownloadPhase.Validating);
            _progress.ReportPhaseChanged(DownloadPhase.Complete);
        });
    }

    [Fact]
    public void ResolveOutputPath_DirectoryPath_CombinesWithBlobFilename()
    {
        var result = DownloadOrchestrator.ResolveOutputPath(
            _tempDir,
            new Uri("https://account.blob.core.windows.net/container/myfile.zip"));

        result.Should().Be(Path.Combine(_tempDir, "myfile.zip"));
    }

    [Fact]
    public void ResolveOutputPath_FilePath_ReturnsAsIs()
    {
        var filePath = Path.Combine(_tempDir, "custom-name.dat");

        var result = DownloadOrchestrator.ResolveOutputPath(
            filePath,
            new Uri("https://account.blob.core.windows.net/container/myfile.zip"));

        result.Should().Be(filePath);
    }

    [Theory]
    [InlineData(1024, 4, 256, 256)]     // 1024/4 = 256, capped at 256 -> 256
    [InlineData(1024, 4, 512, 256)]     // 1024/4 = 256, cap 512 doesn't apply -> 256
    [InlineData(1024, 4, 128, 128)]     // 1024/4 = 256, capped at 128 -> 128
    [InlineData(1000, 3, 500, 334)]     // ceil(1000/3) = 334, cap 500 doesn't apply -> 334
    [InlineData(100, 200, 256, 1)]      // ceil(100/200) = 1 -> 1
    [InlineData(0, 4, 256, 1)]          // edge: zero-size file -> 1
    public void ComputeChunkSize_ReturnsExpectedSize(
        long totalSize, int concurrency, int maxChunkSize, long expected)
    {
        DownloadOrchestrator.ComputeChunkSize(totalSize, concurrency, maxChunkSize)
            .Should().Be(expected);
    }
}
