namespace MW.Projections.Repair;

/// <summary>
/// Asks the owning service to re-emit the canonical event for a single entity so that a
/// consumer's local projection can heal (missing row, stale row) without a synchronous
/// dependency on the owner.
/// </summary>
/// <param name="ReferenceType">
/// The logical name the owning service registered its <see cref="IProjectionRepairSource"/>
/// under (e.g. <c>"product"</c>). Matched case-sensitively against the registry.
/// </param>
/// <param name="ReferenceId">The id of the entity to repair, in the owning service's own format.</param>
/// <param name="CorrelationId">Correlates this repair request with the log trail on both sides.</param>
/// <param name="RequestedAtUtc">When the request was raised, for observability only.</param>
public sealed record ProjectionRepairRequested(
    string ReferenceType,
    string ReferenceId,
    Guid CorrelationId,
    DateTimeOffset RequestedAtUtc);
