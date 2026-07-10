using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MW.Identity.Sso.Grpc;

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
        services.AddDataProtection();

        services.AddAuthentication(options.SchemeName)
            .AddCookie(options.SchemeName, cookie =>
            {
                cookie.Cookie.Name = options.CookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
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
