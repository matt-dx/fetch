using Fetch.Core.Models;

namespace Fetch.Core.Services;

public interface IFileAssembler
{
    /// <summary>Batch assembly: write all chunks into the output file sequentially. Used with --WaitForDownload.</summary>
    Task AssembleAsync(string outputPath, DownloadManifest manifest, CancellationToken ct = default);

    /// <summary>Open a streaming assembly session for writing chunks as they become available.</summary>
    IAssemblySession BeginAssembly(string outputPath, long totalSize);
}

/// <summary>
/// A long-lived handle to the output file. Chunks can be written at any time in any order.
/// Each chunk writes to its own non-overlapping offset range via RandomAccess, so no locking is needed.
/// </summary>
public interface IAssemblySession : IDisposable
{
    /// <summary>Write a single completed chunk into the output file and delete its temp file.</summary>
    Task WriteChunkAsync(ChunkState chunk, CancellationToken ct = default);
}
