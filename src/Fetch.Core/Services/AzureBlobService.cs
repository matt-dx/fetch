using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Fetch.Core.Configuration;
using Fetch.Core.Exceptions;

namespace Fetch.Core.Services;

public sealed class AzureBlobService : IBlobService
{
    private readonly BlobClient _blobClient;
    private readonly Uri _blobUri;

    public AzureBlobService(DownloadOptions options)
    {
        _blobUri = options.BlobUri;

        var clientOptions = new BlobClientOptions
        {
            Retry =
            {
                MaxRetries = 10,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromMinutes(1),
                Mode = RetryMode.Exponential,
                NetworkTimeout = TimeSpan.FromMinutes(5)
            }
        };

        if (!string.IsNullOrEmpty(options.AccountKey))
        {
            var accountName = options.BlobUri.Host.Split('.')[0];
            var credential = new Azure.Storage.StorageSharedKeyCredential(accountName, options.AccountKey);
            _blobClient = new BlobClient(options.BlobUri, credential, clientOptions);
        }
        else
        {
            _blobClient = new BlobClient(options.BlobUri, new DefaultAzureCredential(), clientOptions);
        }
    }

    public async Task<BlobMetadata> GetBlobMetadataAsync(CancellationToken ct = default)
    {
        try
        {
            var properties = await _blobClient.GetPropertiesAsync(cancellationToken: ct);
            var contentLength = properties.Value.ContentLength;
            var contentHash = properties.Value.ContentHash ?? [];
            return new BlobMetadata(contentLength, contentHash);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new BlobNotFoundException(_blobUri, ex);
        }
    }

    public async Task<Stream> OpenChunkStreamAsync(long offset, long length, CancellationToken ct = default)
    {
        var options = new BlobDownloadOptions
        {
            Range = new HttpRange(offset, length)
        };

        var response = await _blobClient.DownloadStreamingAsync(options, ct);
        return response.Value.Content;
    }
}
