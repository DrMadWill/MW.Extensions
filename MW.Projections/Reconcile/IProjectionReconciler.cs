namespace MW.Projections.Reconcile;

/// <summary>
/// Reconciles ONE key's local projection against its remote source of truth. Implemented by
/// <see cref="ProjectionReconcilerBase{TKey,TSnapshot}"/> in this package; consuming services
/// (e.g. listing-service's <c>ProductReferenceReconciler</c>) derive from that base class rather
/// than implementing this interface directly.
/// </summary>
public interface IProjectionReconciler<in TKey>
{
    Task<ProjectionReconcileOutcome> ReconcileAsync(TKey key, CancellationToken cancellationToken);
}
