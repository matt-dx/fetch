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
            var credential = new FallbackInteractiveBrowserTokenCredential(options.InteractiveAuth);
            _blobClient = new BlobClient(options.BlobUri, credential, clientOptions);
        }
    }

    private sealed class FallbackInteractiveBrowserTokenCredential(bool interactiveAuth) : TokenCredential
    {
        private const string NoCredentialMessage =
            "No Azure credential was available. Provide a storage account key with -k, " +
            "configure a supported non-interactive credential (Azure CLI, environment variables, " +
            "managed identity, etc.), or add --interactive to sign in via browser.";

        private readonly TokenCredential[] _nonInteractiveCredentials =
        [
            new EnvironmentCredential(),
            new WorkloadIdentityCredential(),
            new ManagedIdentityCredential(),
            new SharedTokenCacheCredential(),
            new VisualStudioCredential(),
            new VisualStudioCodeCredential(),
            new AzureCliCredential(),
            new AzurePowerShellCredential(),
            new AzureDeveloperCliCredential()
        ];

        private readonly InteractiveBrowserCredential _interactiveCredential = new();
        private readonly object _syncLock = new();
        private TokenCredential? _cachedCredential;

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            var cachedCredential = _cachedCredential;
            if (cachedCredential is not null)
            {
                return cachedCredential.GetToken(requestContext, cancellationToken);
            }

            foreach (var credential in _nonInteractiveCredentials)
            {
                try
                {
                    var token = credential.GetToken(requestContext, cancellationToken);
                    CacheCredential(credential);
                    return token;
                }
                catch (CredentialUnavailableException)
                {
                }
                catch (AuthenticationFailedException)
                {
                }
            }

            if (!interactiveAuth)
            {
                throw new CredentialUnavailableException(NoCredentialMessage);
            }

            var interactiveToken = _interactiveCredential.GetToken(requestContext, cancellationToken);
            CacheCredential(_interactiveCredential);
            return interactiveToken;
        }

        public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            var cachedCredential = _cachedCredential;
            if (cachedCredential is not null)
            {
                return await cachedCredential.GetTokenAsync(requestContext, cancellationToken);
            }

            foreach (var credential in _nonInteractiveCredentials)
            {
                try
                {
                    var token = await credential.GetTokenAsync(requestContext, cancellationToken);
                    CacheCredential(credential);
                    return token;
                }
                catch (CredentialUnavailableException)
                {
                }
                catch (AuthenticationFailedException)
                {
                }
            }

            if (!interactiveAuth)
            {
                throw new CredentialUnavailableException(NoCredentialMessage);
            }

            var interactiveToken = await _interactiveCredential.GetTokenAsync(requestContext, cancellationToken);
            CacheCredential(_interactiveCredential);
            return interactiveToken;
        }

        private void CacheCredential(TokenCredential credential)
        {
            if (_cachedCredential is not null)
            {
                return;
            }

            lock (_syncLock)
            {
                _cachedCredential ??= credential;
            }
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
