using System.Diagnostics;
using Fetcher.Core.Configuration;
using Fetcher.Core.Exceptions;
using Fetcher.Core.Models;
using Fetcher.Core.Services;

namespace Fetcher.Core.Orchestration;

public sealed class DownloadOrchestrator
{
    private readonly IBlobService _blobService;
    private readonly IChunkDownloader _chunkDownloader;
    private readonly IFileAssembler _fileAssembler;
    private readonly IIntegrityValidator _validator;
    private readonly DownloadOptions _options;

    public DownloadOrchestrator(
        IBlobService blobService,
        IChunkDownloader chunkDownloader,
        IFileAssembler fileAssembler,
        IIntegrityValidator validator,
        DownloadOptions options)
    {
        _blobService = blobService;
        _chunkDownloader = chunkDownloader;
        _fileAssembler = fileAssembler;
        _validator = validator;
        _options = options;
    }

    public async Task<DownloadResult> DownloadAsync(
        IDownloadProgress? progress = null,
        CancellationToken ct = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var downloadDuration = TimeSpan.Zero;
        var assemblyDuration = TimeSpan.Zero;
        var validationDuration = TimeSpan.Zero;
        string outputPath = string.Empty;
        DownloadManifest? manifest = null;

        try
        {
            progress?.ReportPhaseChanged(DownloadPhase.Preparing);

            // 1. Get blob metadata
            var metadata = await _blobService.GetBlobMetadataAsync(ct);

            // 2. Resolve output path
            outputPath = ResolveOutputPath(_options.LocalPath, _options.BlobUri);

            // 3. Check if file already exists with correct size
            if (File.Exists(outputPath))
            {
                var existingSize = new FileInfo(outputPath).Length;
                if (existingSize == metadata.ContentLength)
                    throw new FileAlreadyExistsException(outputPath, existingSize);
            }

            // 4. Load or create manifest
            var manifestPath = DownloadManifest.GetManifestPath(outputPath);
            manifest = await LoadOrCreateManifestAsync(manifestPath, metadata, outputPath, ct);

            // 5. Report metadata and seed progress with existing chunk state (for resume)
            progress?.ReportMetadata(manifest.TotalSize, manifest.Chunks.Count);
            foreach (var chunk in manifest.Chunks)
            {
                if (chunk.BytesWritten > 0)
                    progress?.ReportBytesWritten(chunk.Index, chunk.BytesWritten);
                if (chunk.IsComplete)
                    progress?.ReportChunkCompleted(chunk.Index);
            }

            progress?.ReportPhaseChanged(DownloadPhase.Downloading);
            var downloadStopwatch = Stopwatch.StartNew();

            await DownloadChunksAsync(manifest, progress, ct);

            downloadDuration = downloadStopwatch.Elapsed;

            // 6. Save manifest before assembly (for resume if assembly fails)
            await manifest.SaveAsync(manifestPath, ct);

            // 7. Assemble
            progress?.ReportPhaseChanged(DownloadPhase.Assembling);
            var assemblyStopwatch = Stopwatch.StartNew();

            await _fileAssembler.AssembleAsync(outputPath, manifest, ct);

            assemblyDuration = assemblyStopwatch.Elapsed;

            // 8. Validate
            if (metadata.ContentHash.Length > 0)
            {
                progress?.ReportPhaseChanged(DownloadPhase.Validating);
                var validationStopwatch = Stopwatch.StartNew();

                var isValid = await _validator.ValidateAsync(outputPath, metadata.ContentHash, ct);
                validationDuration = validationStopwatch.Elapsed;

                if (!isValid)
                {
                    throw new IntegrityException(
                        metadata.ContentHash,
                        []);  // Actual hash not available here; validator returns bool
                }
            }

            // 9. Cleanup manifest on success
            if (File.Exists(manifestPath))
                File.Delete(manifestPath);

            progress?.ReportPhaseChanged(DownloadPhase.Complete);

            return new DownloadResult
            {
                Success = true,
                OutputPath = outputPath,
                TotalBytes = metadata.ContentLength,
                DownloadDuration = downloadDuration,
                AssemblyDuration = assemblyDuration,
                ValidationDuration = validationDuration,
                TotalDuration = totalStopwatch.Elapsed
            };
        }
        catch (Exception ex) when (ex is not FileAlreadyExistsException and not BlobNotFoundException)
        {
            progress?.ReportPhaseChanged(DownloadPhase.Failed);

            // Save manifest for resume on failure
            if (manifest is not null && outputPath.Length > 0)
            {
                try
                {
                    var manifestPath = DownloadManifest.GetManifestPath(outputPath);
                    await manifest.SaveAsync(manifestPath, CancellationToken.None);
                }
                catch
                {
                    // Best effort — don't mask the original exception
                }
            }

            return new DownloadResult
            {
                Success = false,
                OutputPath = outputPath,
                TotalBytes = manifest?.TotalSize ?? 0,
                DownloadDuration = downloadDuration,
                AssemblyDuration = assemblyDuration,
                ValidationDuration = validationDuration,
                TotalDuration = totalStopwatch.Elapsed,
                Error = ex
            };
        }
    }

    private async Task<DownloadManifest> LoadOrCreateManifestAsync(
        string manifestPath, BlobMetadata metadata, string outputPath, CancellationToken ct)
    {
        var existing = await DownloadManifest.LoadAsync(manifestPath, ct);

        if (existing is not null
            && existing.BlobUri == _options.BlobUri
            && existing.TotalSize == metadata.ContentLength
            && existing.ContentHash.AsSpan().SequenceEqual(metadata.ContentHash))
        {
            existing.AssignTempFilePaths(outputPath);
            existing.SyncBytesWrittenFromDisk();
            return existing;
        }

        // Chunk size = total size / concurrency so every thread gets work, capped by MaxChunkSizeBytes
        var chunkSize = ComputeChunkSize(metadata.ContentLength, _options.MaxConcurrency, _options.MaxChunkSizeBytes);
        var chunkCount = (int)Math.Ceiling((double)metadata.ContentLength / chunkSize);
        var chunks = new List<ChunkState>(chunkCount);

        for (var i = 0; i < chunkCount; i++)
        {
            var offset = (long)i * chunkSize;
            var length = Math.Min(chunkSize, metadata.ContentLength - offset);

            chunks.Add(new ChunkState
            {
                Index = i,
                Offset = offset,
                Length = length,
                TempFilePath = $"{outputPath}.{i:D6}"
            });
        }

        return new DownloadManifest
        {
            BlobUri = _options.BlobUri,
            TotalSize = metadata.ContentLength,
            ContentHash = metadata.ContentHash,
            ChunkSizeBytes = (int)chunkSize,
            Chunks = chunks
        };
    }

    private async Task DownloadChunksAsync(
        DownloadManifest manifest, IDownloadProgress? progress, CancellationToken ct)
    {
        var incompleteChunks = manifest.Chunks.Where(c => !c.IsComplete).ToList();

        if (incompleteChunks.Count == 0)
            return;

        using var semaphore = new SemaphoreSlim(_options.MaxConcurrency);

        var tasks = incompleteChunks.Select(async chunk =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await _chunkDownloader.DownloadChunkAsync(chunk, _blobService, progress, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    internal static long ComputeChunkSize(long totalSize, int maxConcurrency, int maxChunkSizeBytes)
    {
        if (totalSize <= 0 || maxConcurrency <= 0)
            return Math.Max(totalSize, 1);

        // Divide evenly so every thread gets work, capped by MaxChunkSizeBytes
        var chunkSize = (long)Math.Ceiling((double)totalSize / maxConcurrency);
        chunkSize = Math.Min(chunkSize, maxChunkSizeBytes);
        return Math.Max(chunkSize, 1);
    }

    internal static string ResolveOutputPath(string localPath, Uri blobUri)
    {
        // If localPath looks like a directory (exists as dir, or ends with separator, or has no extension)
        if (Directory.Exists(localPath))
        {
            var fileName = Path.GetFileName(blobUri.LocalPath);
            return Path.Combine(localPath, fileName);
        }

        // If path has an extension or parent dir exists, treat as file path
        return localPath;
    }
}
