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

    /// <summary>Name of the shared auth cookie. MUST be identical across all RPs for single sign-on/logout.</summary>
    public string CookieName { get; set; } = "MyBid.Sso";

    /// <summary>
    /// Parent domain for the shared auth cookie (e.g. <c>.mybid.staging</c>). When set, the cookie is
    /// visible to every RP subdomain (single login without re-redirect; single logout via one delete).
    /// Empty = host-only cookie (per-RP, no cross-app sharing).
    /// </summary>
    public string CookieDomain { get; set; } = string.Empty;

    /// <summary>Cookie authentication scheme name registered by <c>AddMyBidSsoRelyingParty</c>.</summary>
    public string SchemeName { get; set; } = "MyBidSso";

    /// <summary>
    /// Redis connection string for the shared Data Protection key ring. When set, all RPs + Identity that
    /// share it can decrypt each other's cookie. Empty = local key ring (single-app dev only — cross-app
    /// cookie decrypt WILL fail, so staging must set this).
    /// </summary>
    public string DataProtectionRedisConnection { get; set; } = string.Empty;

    /// <summary>
    /// Data Protection application name — MUST be identical across all RPs + Identity, otherwise the derived
    /// key purpose differs and cross-app cookie decrypt fails even with a shared key ring.
    /// </summary>
    public string DataProtectionApplicationName { get; set; } = "MyBid.Sso";

    /// <summary>
    /// Identity's central <c>/sso/logout</c> absolute URL. The RP "Çıxış" action redirects here so the shared
    /// cookie + Identity SSO session are cleared centrally (single logout). Empty = local sign-out only.
    /// </summary>
    public string LogoutUrl { get; set; } = string.Empty;

    /// <summary>
    /// Where Identity redirects the browser after central logout (must be on Identity's post-logout allowlist,
    /// i.e. same origin as this RP's <see cref="RedirectUri"/>).
    /// </summary>
    public string PostLogoutRedirectUri { get; set; } = string.Empty;

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
