namespace MW.Projections.Reconcile;

/// <summary>
/// Result of a single <see cref="IProjectionReconciler{TKey}.ReconcileAsync"/> call. Every code
/// path through <see cref="ProjectionReconcilerBase{TKey,TSnapshot}"/> returns exactly one of
/// these — callers (a read-through admin request, a sweep) decide what to do with each outcome,
/// the reconciler itself never throws for an expected divergence.
/// </summary>
public enum ProjectionReconcileOutcome
{
    /// <summary>No local row existed; one was created from the remote snapshot.</summary>
    Created,

    /// <summary>A local row existed but differed from the remote snapshot; it was updated.</summary>
    Updated,

    /// <summary>A local row existed and already matched the remote snapshot; nothing was written.</summary>
    Unchanged,

    /// <summary>
    /// The remote source has no record of this key. The local row (if any) is left untouched —
    /// a reconciler must never delete local data just because the source could not confirm it.
    /// </summary>
    SourceMissing,

    /// <summary>
    /// The remote source could not be reached (e.g. an RPC failure). The local row (if any) is
    /// left untouched; this must never surface as a hard failure to an interactive caller.
    /// </summary>
    SourceUnavailable
}
