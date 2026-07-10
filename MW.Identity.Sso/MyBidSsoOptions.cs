namespace MW.Identity.Sso;

/// <summary>
/// Configuration for a MyBid admin SSO relying party (RP). Each admin WebUI supplies these values;
/// the shared middleware handles the redirect → Identity → callback → cookie sign-in flow.
/// </summary>
public sealed class MyBidSsoOptions
{
    /// <summary>Identity's <c>/sso/authorize</c> absolute URL.</summary>
    public string AuthorizeUrl { get; set; } = string.Empty;

    /// <summary>Identity's SSO introspection gRPC base address (e.g. <c>https://identity:18001</c>).</summary>
    public string GrpcAddress { get; set; } = string.Empty;

    /// <summary>This RP's client id, as registered in Identity's <c>Sso:Clients</c>.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>This RP's client secret (raw); sent as gRPC metadata for introspection auth.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>This RP's absolute callback URL (must be on Identity's redirect allowlist).</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>Local callback path handled by <c>MapMyBidSsoCallback</c>.</summary>
    public string CallbackPath { get; set; } = "/sso/callback";

    /// <summary>Name of the local auth cookie.</summary>
    public string CookieName { get; set; } = "MyBid.Sso";

    /// <summary>Cookie authentication scheme name registered by <c>AddMyBidSsoRelyingParty</c>.</summary>
    public string SchemeName { get; set; } = "MyBidSso";

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(AuthorizeUrl))
        {
            throw new InvalidOperationException("MyBidSsoOptions.AuthorizeUrl is required.");
        }

        if (string.IsNullOrWhiteSpace(GrpcAddress))
        {
            throw new InvalidOperationException("MyBidSsoOptions.GrpcAddress is required.");
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("MyBidSsoOptions.ClientId is required.");
        }

        if (string.IsNullOrWhiteSpace(RedirectUri))
        {
            throw new InvalidOperationException("MyBidSsoOptions.RedirectUri is required.");
        }

        if (string.IsNullOrWhiteSpace(CallbackPath) || !CallbackPath.StartsWith('/'))
        {
            throw new InvalidOperationException("MyBidSsoOptions.CallbackPath must be a root-relative path.");
        }
    }
}
