using System.Diagnostics;
using System.Threading.Channels;
using Fetch.Core.Configuration;
using Fetch.Core.Exceptions;
using Fetch.Core.Models;
using Fetch.Core.Services;

namespace Fetch.Core.Orchestration;

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

        var hidden = !_options.ShowChunks;
        string manifestPath = string.Empty;

        try
        {
            progress?.ReportPhaseChanged(DownloadPhase.Preparing);

            // 1. Get blob metadata
            var metadata = await _blobService.GetBlobMetadataAsync(ct);

            // 2. Resolve output path
            outputPath = ResolveOutputPath(_options.LocalPath, _options.BlobUri);
            var assemblyPath = outputPath + ".part";

            // 3. Check for existing manifest (resume) or completed file
            manifestPath = ChunkNaming.FindExistingManifest(outputPath)
                ?? DownloadManifest.GetManifestPath(outputPath, hidden);
            var existingManifest = await DownloadManifest.LoadAsync(manifestPath, ct);

            // Only treat the file as "already complete" if there's no valid manifest,
            // no chunk temp files, and no .part file on disk.
            if (existingManifest is null && File.Exists(outputPath) && !File.Exists(assemblyPath))
            {
                var existingSize = new FileInfo(outputPath).Length;
                var hasChunkFiles = ChunkNaming.FindExistingChunks(outputPath).Any();
                if (existingSize == metadata.ContentLength && !hasChunkFiles)
                    throw new FileAlreadyExistsException(outputPath, existingSize);
            }

            // 4. Load or create manifest (assigns desired chunk paths but does NOT sync bytes yet)
            manifest = await LoadOrCreateManifestAsync(manifestPath, metadata, outputPath, existingManifest, ct);

            // Migrate chunk/manifest files on disk to match current --ShowChunks setting.
            // This renames any files that were created with the opposite visibility convention
            // (e.g., hidden ".file.000001" → visible "file.000001" or vice versa).
            // Must happen BEFORE SyncBytesWrittenFromDisk so the files are at the expected paths.
            manifestPath = ChunkNaming.MigrateVisibility(manifest, outputPath, manifestPath, hidden);

            // Now that files are at the correct paths, pick up actual bytes on disk
            manifest.SyncBytesWrittenFromDisk();

            // 5. Report metadata and seed progress with existing chunk state (for resume)
            progress?.ReportMetadata(manifest.TotalSize, manifest.Chunks.Count);
            foreach (var chunk in manifest.Chunks)
            {
                progress?.ReportChunkInfo(chunk.Index, chunk.Length);
                if (chunk.BytesWritten > 0)
                    progress?.ReportBytesWritten(chunk.Index, chunk.BytesWritten);
                if (chunk.IsComplete)
                    progress?.ReportChunkCompleted(chunk.Index);
            }

            // Seed assembly progress: chunks whose temp files are already gone were assembled previously
            foreach (var chunk in manifest.Chunks.Where(c => c.IsComplete && !File.Exists(c.TempFilePath)))
                progress?.ReportChunkAssembled(chunk.Index);

            // 6. Download (and optionally assemble concurrently)
            progress?.ReportPhaseChanged(DownloadPhase.Downloading);
            var downloadStopwatch = Stopwatch.StartNew();

            if (_options.WaitForDownload)
            {
                await DownloadChunksAsync(manifest, progress, ct);
                downloadDuration = downloadStopwatch.Elapsed;

                // Save manifest before batch assembly
                await manifest.SaveAsync(manifestPath, ct);
                ChunkNaming.SetHiddenAttribute(manifestPath, hidden);

                // Batch assemble into .part file
                progress?.ReportPhaseChanged(DownloadPhase.Assembling);
                var assemblyStopwatch = Stopwatch.StartNew();
                await _fileAssembler.AssembleAsync(assemblyPath, manifest, ct);
                assemblyDuration = assemblyStopwatch.Elapsed;
            }
            else
            {
                await DownloadAndAssembleAsync(manifest, assemblyPath, progress, ct);
                downloadDuration = downloadStopwatch.Elapsed;
                // Assembly happened concurrently; its time is included in downloadDuration
                assemblyDuration = TimeSpan.Zero;

                // Save manifest (will be deleted shortly on success)
                await manifest.SaveAsync(manifestPath, ct);
                ChunkNaming.SetHiddenAttribute(manifestPath, hidden);
            }

            // 7. Validate against the .part file
            if (metadata.ContentHash.Length > 0)
            {
                progress?.ReportPhaseChanged(DownloadPhase.Validating);
                var validationStopwatch = Stopwatch.StartNew();

                var isValid = await _validator.ValidateAsync(assemblyPath, metadata.ContentHash, ct);
                validationDuration = validationStopwatch.Elapsed;

                if (!isValid)
                {
                    throw new IntegrityException(
                        metadata.ContentHash,
                        []);
                }
            }

            // 8. Rename .part to final output
            File.Move(assemblyPath, outputPath, overwrite: true);

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
                    if (string.IsNullOrEmpty(manifestPath))
                        manifestPath = DownloadManifest.GetManifestPath(outputPath, hidden);
                    await manifest.SaveAsync(manifestPath, CancellationToken.None);
                    ChunkNaming.SetHiddenAttribute(manifestPath, hidden);
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

    /// <summary>
    /// Download chunks in parallel and assemble each one as soon as it finishes,
    /// using a Channel as a producer-consumer queue.
    /// </summary>
    private async Task DownloadAndAssembleAsync(
        DownloadManifest manifest, string assemblyPath,
        IDownloadProgress? progress, CancellationToken ct)
    {
        var incompleteChunks = manifest.Chunks.Where(c => !c.IsComplete).ToList();

        // Open the .part file for streaming assembly
        using var session = _fileAssembler.BeginAssembly(assemblyPath, manifest.TotalSize);

        // First, assemble any chunks that were downloaded but not yet assembled (resume case)
        var downloadedButNotAssembled = manifest.Chunks
            .Where(c => c.IsComplete && File.Exists(c.TempFilePath))
            .ToList();

        foreach (var chunk in downloadedButNotAssembled)
        {
            await session.WriteChunkAsync(chunk, ct);
            progress?.ReportChunkAssembled(chunk.Index);
        }

        if (incompleteChunks.Count == 0)
            return;

        // Channel for completed chunks awaiting assembly
        var assemblyQueue = Channel.CreateUnbounded<ChunkState>(
            new UnboundedChannelOptions { SingleReader = true });

        // Assembly consumer task
        var assemblyTask = Task.Run(async () =>
        {
            await foreach (var chunk in assemblyQueue.Reader.ReadAllAsync(ct))
            {
                await session.WriteChunkAsync(chunk, ct);
                progress?.ReportChunkAssembled(chunk.Index);
            }
        }, ct);

        // Download producer tasks
        using var semaphore = new SemaphoreSlim(_options.MaxConcurrency);

        try
        {
            var downloadTasks = incompleteChunks.Select(async chunk =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    await _chunkDownloader.DownloadChunkAsync(chunk, _blobService, progress, ct);
                    await assemblyQueue.Writer.WriteAsync(chunk, ct);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList(); // Force eager evaluation so all tasks start

            await Task.WhenAll(downloadTasks);
        }
        finally
        {
            // Always signal completion so the assembly consumer exits
            assemblyQueue.Writer.Complete();
        }

        await assemblyTask;
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

    private async Task<DownloadManifest> LoadOrCreateManifestAsync(
        string manifestPath, BlobMetadata metadata, string outputPath,
        DownloadManifest? existing, CancellationToken ct)
    {
        var hidden = !_options.ShowChunks;

        if (existing is not null
            && existing.BlobUri == _options.BlobUri
            && existing.TotalSize == metadata.ContentLength
            && existing.ContentHash.AsSpan().SequenceEqual(metadata.ContentHash))
        {
            // Assign desired paths (hidden or visible) — actual file migration
            // and byte sync happen after MigrateVisibility in the caller
            existing.AssignTempFilePaths(outputPath, hidden);
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
                TempFilePath = ChunkNaming.GetChunkPath(outputPath, i, hidden)
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
