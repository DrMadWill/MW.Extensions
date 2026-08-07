namespace MW.Projections.Repair;

/// <summary>
/// Implemented by the service that owns an entity type to re-emit its canonical event for a
/// single id, so that a downstream projection can heal itself. Registered via
/// <c>services.AddProjectionRepairSource&lt;TSource&gt;(referenceType)</c>.
/// <para>
/// A repair is a re-emit of already-true state: implementations must be idempotent by
/// construction (re-running a repair for the same id is harmless).
/// </para>
/// </summary>
public interface IProjectionRepairSource
{
    /// <summary>
    /// Re-emits the canonical event(s) for <paramref name="referenceId"/>. If the id no longer
    /// exists, implementations should complete without throwing — there is nothing to repair.
    /// </summary>
    Task RepairAsync(string referenceId, CancellationToken cancellationToken);
}
