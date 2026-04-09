using Fetcher.Core.Configuration;
using Fetcher.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace Fetcher.Core.Services;

public sealed class FileAssembler : IFileAssembler
{
    private readonly DownloadOptions _options;

    public FileAssembler(DownloadOptions options)
    {
        _options = options;
    }

    public async Task AssembleAsync(string outputPath, DownloadManifest manifest, CancellationToken ct = default)
    {
        using var handle = File.OpenHandle(
            outputPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None,
            FileOptions.Asynchronous);

        RandomAccess.SetLength(handle, manifest.TotalSize);

        // Only assemble chunks whose temp files still exist (skip already-assembled ones)
        var pendingChunks = manifest.Chunks.Where(c => File.Exists(c.TempFilePath)).ToList();

        var semaphore = new SemaphoreSlim(_options.MaxConcurrency);

        var tasks = pendingChunks.Select(async chunk =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await WriteChunkAsync(handle, chunk, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task WriteChunkAsync(SafeFileHandle handle, ChunkState chunk, CancellationToken ct)
    {
        var buffer = new byte[_options.BufferSizeBytes];
        long position = chunk.Offset;
        int bytesRead;

        // Explicit using block ensures the file handle is closed before we delete
        using (var chunkStream = new FileStream(
            chunk.TempFilePath, FileMode.Open, FileAccess.Read, FileShare.None,
            _options.BufferSizeBytes, FileOptions.SequentialScan))
        {
            while ((bytesRead = await chunkStream.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, bytesRead), position, ct);
                position += bytesRead;
            }
        }

        File.Delete(chunk.TempFilePath);
    }
}
