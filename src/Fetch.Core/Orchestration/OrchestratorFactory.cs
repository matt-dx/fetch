using Fetch.Core.Configuration;
using Fetch.Core.Services;

namespace Fetch.Core.Orchestration;

public sealed class OrchestratorFactory : IOrchestratorFactory
{
    public DownloadOrchestrator Create(DownloadOptions options)
    {
        var blobService = new AzureBlobService(options);
        var chunkDownloader = new ChunkDownloader(options);
        var fileAssembler = new FileAssembler(options);
        var validator = new Md5Validator();

        return new DownloadOrchestrator(blobService, chunkDownloader, fileAssembler, validator, options);
    }
}
