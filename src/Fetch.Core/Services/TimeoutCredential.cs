using Azure.Core;

namespace Fetch.Core.Services;

internal sealed class TimeoutCredential(TokenCredential inner, TimeSpan timeout) : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        return inner.GetToken(requestContext, cts.Token);
    }

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        return await inner.GetTokenAsync(requestContext, cts.Token);
    }
}
