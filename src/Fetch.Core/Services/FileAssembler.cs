using Fetch.Core.Configuration;
using Fetch.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace Fetch.Core.Services;

public sealed class FileAssembler : IFileAssembler
{
    private readonly DownloadOptions _options;

    public FileAssembler(DownloadOptions options)
    {
        _options = options;
    }

    public async Task AssembleAsync(string outputPath, DownloadManifest manifest, CancellationToken ct = default)
    {
        using var session = BeginAssembly(outputPath, manifest.TotalSize);

        // Only assemble chunks whose temp files still exist (skip already-assembled ones)
        var pendingChunks = manifest.Chunks.Where(c => File.Exists(c.TempFilePath)).ToList();

        var semaphore = new SemaphoreSlim(_options.MaxConcurrency);

        var tasks = pendingChunks.Select(async chunk =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await session.WriteChunkAsync(chunk, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    public IAssemblySession BeginAssembly(string outputPath, long totalSize)
    {
        return new AssemblySession(outputPath, totalSize, _options.BufferSizeBytes);
    }

    private sealed class AssemblySession : IAssemblySession
    {
        private readonly SafeFileHandle _handle;
        private readonly int _bufferSize;

        public AssemblySession(string outputPath, long totalSize, int bufferSize)
        {
            _bufferSize = bufferSize;
            _handle = File.OpenHandle(
                outputPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None,
                FileOptions.Asynchronous);
            RandomAccess.SetLength(_handle, totalSize);
        }

        public async Task WriteChunkAsync(ChunkState chunk, CancellationToken ct = default)
        {
            if (!File.Exists(chunk.TempFilePath))
                return;

            var buffer = new byte[_bufferSize];
            long position = chunk.Offset;
            int bytesRead;

            using (var chunkStream = new FileStream(
                chunk.TempFilePath, FileMode.Open, FileAccess.Read, FileShare.None,
                _bufferSize, FileOptions.SequentialScan))
            {
                while ((bytesRead = await chunkStream.ReadAsync(buffer.AsMemory(), ct)) > 0)
                {
                    await RandomAccess.WriteAsync(_handle, buffer.AsMemory(0, bytesRead), position, ct);
                    position += bytesRead;
                }
            }

            File.Delete(chunk.TempFilePath);
        }

        public void Dispose()
        {
            _handle.Dispose();
        }
    }
}
