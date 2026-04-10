namespace Fetch.Core.Services;

public record BlobMetadata(long ContentLength, byte[] ContentHash);

public interface IBlobService
{
    Task<BlobMetadata> GetBlobMetadataAsync(CancellationToken ct = default);
    Task<Stream> OpenChunkStreamAsync(long offset, long length, CancellationToken ct = default);
}
