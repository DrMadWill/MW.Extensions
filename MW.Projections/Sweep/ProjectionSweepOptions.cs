namespace MW.Projections.Sweep;

/// <summary>
/// Options for <see cref="Extensions.ProjectionsServiceCollectionExtensions.AddProjectionSweep{TKey,TSource,TReconciler}"/>.
/// </summary>
public sealed class ProjectionSweepOptions
{
    /// <summary>
    /// Off by default. A sweep is a background write loop over potentially the whole keyspace —
    /// it must be turned on deliberately, on exactly ONE host, by the consuming service (see
    /// Task 9: enabled on the API host only, never the WebUI host, so it does not run twice).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>How often a new sweep run starts after the previous one finished. Default: 15 minutes.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Keys requested per page from <see cref="IProjectionSweepSource{TKey}"/>. Default: 200.</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>
    /// Safety cap on batches processed in a single run — mirrors <c>SyncListingReferencesCommand</c>'s
    /// <c>MaxPages</c>: protects against a misbehaving source that never returns a short final page,
    /// not an expected ceiling for a healthy keyspace. Default: 50 (10,000 keys at the default
    /// batch size).
    /// </summary>
    public int MaxBatchesPerRun { get; set; } = 50;
}
