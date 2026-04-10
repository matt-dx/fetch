using Fetch.Core.Models;
using Fetch.Core.Orchestration;

namespace Fetch.Core.Services;

public interface IChunkDownloader
{
    Task DownloadChunkAsync(
        ChunkState chunk,
        IBlobService blobService,
        IDownloadProgress? progress = null,
        CancellationToken ct = default);
}
