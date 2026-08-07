namespace MW.Projections.Sweep;

/// <summary>
/// Pages through the keys a sweep should reconcile, cursor-based (not offset-based, to stay stable
/// while rows are concurrently created/updated by the same sweep — see
/// <c>SyncListingReferencesCommand</c>'s paging precedent). Implemented by the consuming service
/// against its own storage; this package never sees the query.
/// </summary>
public interface IProjectionSweepSource<TKey>
{
    /// <summary>
    /// Returns up to <paramref name="batchSize"/> keys strictly after <paramref name="cursor"/>
    /// (or the first page when <paramref name="cursor"/> is <see langword="null"/>). An empty
    /// result means the sweep has reached the end of the keyspace for this run.
    /// </summary>
    Task<IReadOnlyList<TKey>> GetNextBatchAsync(int batchSize, TKey? cursor, CancellationToken cancellationToken);
}
