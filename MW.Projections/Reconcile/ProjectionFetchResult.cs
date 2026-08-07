namespace MW.Projections.Reconcile;

/// <summary>
/// Outcome of <see cref="ProjectionReconcilerBase{TKey,TSnapshot}.FetchAsync"/> alone (before any
/// local comparison happens).
/// </summary>
public enum ProjectionFetchStatus
{
    /// <summary>The remote source has a snapshot for the key.</summary>
    Found,

    /// <summary>The remote source has no record of the key at all.</summary>
    NotFound,

    /// <summary>The remote source could not be reached.</summary>
    Unavailable
}

/// <summary>
/// Plain-type result of a remote fetch. Exists so <see cref="ProjectionReconcilerBase{TKey,TSnapshot}"/>
/// never needs to know what kind of failure makes a source "unavailable" — a concrete
/// <c>FetchAsync</c> override (e.g. listing-service's gRPC client) is the one place that catches a
/// transport exception (an <c>RpcException</c>, say) and translates it to
/// <see cref="Unavailable"/>. This is what keeps this package free of any gRPC (or EF Core)
/// reference: the base class only ever sees <see cref="ProjectionFetchStatus"/> and
/// <typeparamref name="TSnapshot"/>, never the exception type that produced them.
/// </summary>
public readonly struct ProjectionFetchResult<TSnapshot>
    where TSnapshot : class
{
    private ProjectionFetchResult(ProjectionFetchStatus status, TSnapshot? snapshot)
    {
        Status = status;
        Snapshot = snapshot;
    }

    public ProjectionFetchStatus Status { get; }

    /// <summary>Non-null only when <see cref="Status"/> is <see cref="ProjectionFetchStatus.Found"/>.</summary>
    public TSnapshot? Snapshot { get; }

    public static ProjectionFetchResult<TSnapshot> Found(TSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ProjectionFetchResult<TSnapshot>(ProjectionFetchStatus.Found, snapshot);
    }

    public static ProjectionFetchResult<TSnapshot> NotFound() =>
        new(ProjectionFetchStatus.NotFound, null);

    public static ProjectionFetchResult<TSnapshot> Unavailable() =>
        new(ProjectionFetchStatus.Unavailable, null);
}
