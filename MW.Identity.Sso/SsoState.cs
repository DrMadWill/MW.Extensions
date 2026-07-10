using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace MW.Identity.Sso;

/// <summary>
/// Helpers for the CSRF <c>state</c> round-trip: a random state paired with the original return URL is
/// data-protected into a short-lived cookie on challenge, and verified on callback.
/// </summary>
internal static class SsoState
{
    internal const string StateCookieName = "MyBid.Sso.State";
    private const string ProtectorPurpose = "MW.Identity.Sso.State.v1";

    public static string Generate()
        => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string BuildAuthorizeUrl(MyBidSsoOptions options, string state)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri,
            ["state"] = state
        };
        return QueryHelpers.AddQueryString(options.AuthorizeUrl, query);
    }

    public static void WriteCookie(HttpContext http, string state, string returnUrl)
    {
        var payload = Protector(http).Protect($"{state}|{returnUrl}");
        http.Response.Cookies.Append(StateCookieName, payload, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = http.Request.IsHttps,
            IsEssential = true,
            MaxAge = TimeSpan.FromMinutes(5),
            Path = "/"
        });
    }

    public static bool TryReadCookie(HttpContext http, out string state, out string returnUrl)
    {
        state = string.Empty;
        returnUrl = string.Empty;

        var raw = http.Request.Cookies[StateCookieName];
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        try
        {
            var payload = Protector(http).Unprotect(raw);
            var separator = payload.IndexOf('|');
            if (separator < 0)
            {
                return false;
            }

            state = payload[..separator];
            returnUrl = payload[(separator + 1)..];
            return state.Length > 0;
        }
        catch (CryptographicException)
        {
            // Tampered or stale cookie.
            return false;
        }
    }

    public static void DeleteCookie(HttpContext http)
        => http.Response.Cookies.Delete(StateCookieName, new CookieOptions { Path = "/" });

    public static bool FixedEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    public static bool IsLocalUrl(string url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
        {
            return false;
        }

        // Reject protocol-relative ("//host") and backslash tricks ("/\host").
        return url.Length == 1 || (url[1] != '/' && url[1] != '\\');
    }

    private static IDataProtector Protector(HttpContext http)
        => http.RequestServices.GetDataProtector(ProtectorPurpose);
}
