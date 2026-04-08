using Fetcher.Core.Models;
using Fetcher.Core.Orchestration;

namespace Fetcher.Core.Services;

public interface IChunkDownloader
{
    Task DownloadChunkAsync(
        ChunkState chunk,
        IBlobService blobService,
        IDownloadProgress? progress = null,
        CancellationToken ct = default);
}
