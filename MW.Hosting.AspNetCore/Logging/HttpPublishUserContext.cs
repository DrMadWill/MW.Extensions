using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MW.Messaging.Context;

namespace MW.Hosting.AspNetCore.Logging;

/// <summary>
/// Supplies UserId/TenantId to published events from the current HTTP request's claims.
/// UserId ← ClaimTypes.NameIdentifier; TenantId ← "tenant_id" claim. Null when no HttpContext
/// or the claim is missing/unparseable.
/// </summary>
public class HttpPublishUserContext : IPublishUserContext
{
    private const string TenantClaimType = "tenant_id";
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpPublishUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId =>
        ParseGuid(_httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value);

    public Guid? TenantId =>
        ParseGuid(_httpContextAccessor.HttpContext?.User?.FindFirst(TenantClaimType)?.Value);

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var id) ? id : null;
}
