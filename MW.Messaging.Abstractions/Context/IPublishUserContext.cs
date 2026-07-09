namespace MW.Messaging.Context;

/// <summary>
/// Supplies the current user and tenant identifiers to be attached to published events
/// (mapped to the x-user-id / x-tenant-id headers). Implemented by the host layer
/// (e.g. MW.Hosting.AspNetCore from HTTP claims). Optional: when no implementation is
/// registered, user/tenant simply remain null.
/// </summary>
public interface IPublishUserContext
{
    Guid? UserId { get; }
    Guid? TenantId { get; }
}
