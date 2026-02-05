using Azure.Core;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using MsalAccessToken = Microsoft.AspNetCore.Components.WebAssembly.Authentication.AccessToken;
using AzureAccessToken = Azure.Core.AccessToken;

namespace NoShowPredictor.Web.Services;

/// <summary>
/// A TokenCredential implementation that bridges MSAL tokens from Blazor WASM 
/// to Azure SDK clients.
/// </summary>
public sealed class MsalTokenCredential : TokenCredential
{
    private readonly IAccessTokenProvider _tokenProvider;
    private readonly string[] _scopes;

    public MsalTokenCredential(IAccessTokenProvider tokenProvider, string[] scopes)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
    }

    public override AzureAccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        // Synchronous version - not ideal but required by interface
        return GetTokenAsync(requestContext, cancellationToken).GetAwaiter().GetResult();
    }

    public override async ValueTask<AzureAccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var result = await _tokenProvider.RequestAccessToken(new AccessTokenRequestOptions
        {
            Scopes = _scopes
        });

        if (result.TryGetToken(out var token))
        {
            return new AzureAccessToken(token.Value, token.Expires);
        }

        throw new InvalidOperationException("Failed to acquire MSAL token. User may need to sign in.");
    }
}
