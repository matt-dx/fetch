using Azure;
using Fetcher.Core.Configuration;
using Fetcher.Core.Exceptions;
using Fetcher.Core.Models;
using Fetcher.Core.Orchestration;

namespace Fetcher.Core.Services;

public sealed class ChunkDownloader : IChunkDownloader
{
    private readonly DownloadOptions _options;

    public ChunkDownloader(DownloadOptions options)
    {
        _options = options;
    }

    public async Task DownloadChunkAsync(
        ChunkState chunk,
        IBlobService blobService,
        IDownloadProgress? progress = null,
        CancellationToken ct = default)
    {
        if (chunk.IsComplete)
            return;

        var attempt = 0;

        while (true)
        {
            try
            {
                await DownloadChunkCoreAsync(chunk, blobService, progress, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && IsTransient(ex))
            {
                attempt++;

                if (attempt >= _options.MaxRetriesPerChunk)
                {
                    throw new DownloadException(
                        $"Chunk {chunk.Index} failed after {attempt} retries",
                        chunk.Index, chunk.Offset, chunk.BytesWritten, ex);
                }

                progress?.ReportError(chunk.Index, ex, attempt);

                // Re-stat file to get actual bytes on disk after partial write
                SyncBytesFromDisk(chunk);

                var delay = _options.RetryBaseDelay * Math.Pow(2, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(delay.TotalMilliseconds), ct);
            }
        }
    }

    private async Task DownloadChunkCoreAsync(
        ChunkState chunk,
        IBlobService blobService,
        IDownloadProgress? progress,
        CancellationToken ct)
    {
        var fileMode = chunk.BytesWritten > 0 ? FileMode.Append : FileMode.Create;

        await using var blobStream = await blobService.OpenChunkStreamAsync(
            chunk.ResumeOffset, chunk.BytesRemaining, ct);

        await using var fileStream = new FileStream(
            chunk.TempFilePath, fileMode, FileAccess.Write, FileShare.None,
            _options.BufferSizeBytes, FileOptions.Asynchronous);

        var buffer = new byte[_options.BufferSizeBytes];
        int bytesRead;

        while ((bytesRead = await blobStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            chunk.BytesWritten += bytesRead;
            progress?.ReportBytesWritten(chunk.Index, chunk.BytesWritten);
        }

        progress?.ReportChunkCompleted(chunk.Index);
    }

    private static void SyncBytesFromDisk(ChunkState chunk)
    {
        if (File.Exists(chunk.TempFilePath))
        {
            chunk.BytesWritten = new FileInfo(chunk.TempFilePath).Length;
        }
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        IOException => true,
        RequestFailedException rfe => rfe.Status is 408 or 429 or 500 or 502 or 503 or 504,
        _ => false
    };
}
