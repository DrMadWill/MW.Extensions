using System.Security.Claims;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MW.Identity.Sso.Grpc;
using MW.Identity.Token.Constants;

namespace MW.Identity.Sso;

/// <summary>
/// Endpoint + sign-out helpers for the MyBid SSO relying party. <c>MapMyBidSsoCallback</c> handles the
/// redirect back from Identity: verify state, exchange the code over gRPC, then establish the local
/// cookie session from the returned claims.
/// </summary>
public static class MyBidSsoEndpointExtensions
{
    public static IEndpointRouteBuilder MapMyBidSsoCallback(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<MyBidSsoOptions>();

        endpoints.MapGet(options.CallbackPath, async (
            HttpContext http,
            SsoIntrospectionRpc.SsoIntrospectionRpcClient client) =>
        {
            var code = http.Request.Query["code"].ToString();
            var state = http.Request.Query["state"].ToString();
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            {
                return Results.BadRequest("code və state tələb olunur.");
            }

            if (!SsoState.TryReadCookie(http, out var expectedState, out var returnUrl)
                || !SsoState.FixedEquals(state, expectedState))
            {
                return Results.BadRequest("state uyğunsuzluğu.");
            }
            SsoState.DeleteCookie(http);

            ExchangeAuthCodeReply reply;
            try
            {
                var metadata = new Metadata
                {
                    { "x-client-id", options.ClientId },
                    { "x-client-secret", options.ClientSecret }
                };
                reply = await client.ExchangeAuthCodeAsync(
                    new ExchangeAuthCodeRequest { Code = code },
                    metadata,
                    cancellationToken: http.RequestAborted);
            }
            catch (RpcException ex)
            {
                var status = ex.StatusCode == StatusCode.Unauthenticated
                    ? StatusCodes.Status401Unauthorized
                    : StatusCodes.Status400BadRequest;
                return Results.StatusCode(status);
            }

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, reply.UserId) };
            claims.AddRange(reply.Claims.Select(pair => new Claim(pair.Type, pair.Value)));
            claims.AddRange(reply.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
            claims.AddRange(reply.Permissions.Select(permission => new Claim("permission", permission)));

            var identity = new ClaimsIdentity(claims, options.SchemeName, ClaimConstants.UserName, ClaimTypes.Role);
            var principal = new ClaimsPrincipal(identity);

            var props = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.FromUnixTimeSeconds(reply.ExpiresAtUnix)
            };
            props.StoreTokens([new AuthenticationToken { Name = "access_token", Value = reply.AccessToken }]);

            await http.SignInAsync(options.SchemeName, principal, props);

            var target = SsoState.IsLocalUrl(returnUrl) ? returnUrl : "/";
            return Results.Redirect(target);
        }).AllowAnonymous();

        return endpoints;
    }

    /// <summary>
    /// Signs the user out of the local SSO cookie session. Single-logout propagation to Identity is
    /// Phase 2; for now a local cookie sign-out is sufficient.
    /// </summary>
    public static Task SignOutMyBidSsoAsync(this HttpContext http)
    {
        var options = http.RequestServices.GetRequiredService<MyBidSsoOptions>();
        return http.SignOutAsync(options.SchemeName);
    }
}
