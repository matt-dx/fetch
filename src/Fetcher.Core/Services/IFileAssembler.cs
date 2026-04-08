using Fetcher.Core.Models;

namespace Fetcher.Core.Services;

public interface IFileAssembler
{
    Task AssembleAsync(string outputPath, DownloadManifest manifest, CancellationToken ct = default);
}
