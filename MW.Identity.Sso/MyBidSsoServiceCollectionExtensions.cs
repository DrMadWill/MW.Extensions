using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MW.Identity.Sso.Grpc;
using StackExchange.Redis;

namespace MW.Identity.Sso;

/// <summary>
/// Registers a MyBid admin SSO relying party: a cookie auth scheme whose challenge redirects to
/// Identity's <c>/sso/authorize</c>, plus the introspection gRPC client. Pair with
/// <c>MapMyBidSsoCallback</c> to complete the flow.
/// </summary>
public static class MyBidSsoServiceCollectionExtensions
{
    public static IServiceCollection AddMyBidSsoRelyingParty(this IServiceCollection services, Action<MyBidSsoOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MyBidSsoOptions();
        configure(options);
        options.Validate();

        services.AddSingleton(options);

        // Shared Data Protection key ring: a shared parent-domain cookie is only decryptable across apps
        // when every RP + Identity use the same key ring AND the same application name. The Redis persistence
        // is optional (local dev falls back to a per-app key ring — cross-app decrypt then fails by design).
        var dataProtection = services.AddDataProtection()
            .SetApplicationName(options.DataProtectionApplicationName);
        if (!string.IsNullOrWhiteSpace(options.DataProtectionRedisConnection))
        {
            var redis = ConnectionMultiplexer.Connect(options.DataProtectionRedisConnection);
            dataProtection.PersistKeysToStackExchangeRedis(redis, "DataProtection-Keys:MyBid.Sso");
        }

        services.AddAuthentication(options.SchemeName)
            .AddCookie(options.SchemeName, cookie =>
            {
                cookie.Cookie.Name = options.CookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                // SameAsRequest (not Always): staging RPs are served over plain HTTP behind WireGuard, and a
                // Secure=Always cookie would never be sent there, breaking SSO. Harden to Always once staging
                // terminates TLS. Lax is safe: the RP↔Identity redirects are top-level GET navigations.
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                if (!string.IsNullOrWhiteSpace(options.CookieDomain))
                {
                    cookie.Cookie.Domain = options.CookieDomain;
                }

                cookie.Events = new CookieAuthenticationEvents
                {
                    // Unauthenticated requests bounce to Identity's SSO authorize endpoint (not a local
                    // login page); the originally requested URL is preserved as the return target.
                    OnRedirectToLogin = context =>
                    {
                        var state = SsoState.Generate();
                        var returnUrl = context.Request.Path + context.Request.QueryString;
                        SsoState.WriteCookie(context.HttpContext, state, returnUrl);
                        context.Response.Redirect(SsoState.BuildAuthorizeUrl(options, state));
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddGrpcClient<SsoIntrospectionRpc.SsoIntrospectionRpcClient>(client =>
        {
            client.Address = new Uri(options.GrpcAddress);
        });

        return services;
    }
}
