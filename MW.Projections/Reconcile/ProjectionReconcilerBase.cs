using Microsoft.Extensions.Logging;
using MW.Projections.Sweep;

namespace MW.Projections.Reconcile;

/// <summary>
/// Base implementation of <see cref="IProjectionReconciler{TKey}"/>: fetch the remote snapshot,
/// load the local row, and either create, update, or leave it alone. Concrete subclasses supply
/// only plain-type storage/transport access — this class never references EF Core or gRPC types
/// (the package-wide hard constraint).
/// <para>
/// <b>Write-race handling</b> mirrors <c>ProvisionFirstPartyListingCommandHandler</c>: the normal
/// path is read-then-decide (<see cref="LoadLocalAsync"/> before <see cref="CreateAsync"/>/
/// <see cref="UpdateAsync"/>, exactly like <c>SyncListingReferencesCommand</c>'s
/// read-then-create-or-update), so a blind insert-and-catch is NOT the primary strategy. But two
/// reconcile calls for the same key CAN still race each other (a sweep and a read-through, or two
/// sweep batches) between the read and the write. For that narrow window,
/// <see cref="CreateAsync"/> is wrapped in a catch that recognizes a duplicate-insert failure by
/// its RUNTIME TYPE NAME (default: <c>"DbUpdateException"</c>) rather than a compile-time
/// reference to <c>Microsoft.EntityFrameworkCore.DbUpdateException</c> — the same technique
/// <c>ProvisionFirstPartyListingCommandHandler.IsPersistenceUpdateFailure</c> uses to stay
/// EF-agnostic. A caught failure is treated as benign ONLY after re-verifying the post-condition
/// (a local row now exists for the key) — a save failure alone does not prove a concurrent writer
/// won; anything else rethrows.
/// </para>
/// </summary>
/// <typeparam name="TKey">The entity key type (e.g. <see cref="Guid"/>).</typeparam>
/// <typeparam name="TSnapshot">The plain-type remote/local comparison shape.</typeparam>
public abstract class ProjectionReconcilerBase<TKey, TSnapshot> : IProjectionReconciler<TKey>
    where TSnapshot : class
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    protected ProjectionReconcilerBase(TimeProvider timeProvider, ILogger logger)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Fetches the remote snapshot for <paramref name="key"/>. Implementations own translating any
    /// transport failure (e.g. an <c>RpcException</c>) into <see cref="ProjectionFetchResult{TSnapshot}.Unavailable"/> —
    /// this base class never sees the exception type.
    /// </summary>
    protected abstract Task<ProjectionFetchResult<TSnapshot>> FetchAsync(TKey key, CancellationToken cancellationToken);

    /// <summary>Loads the current local row for <paramref name="key"/>, or <see langword="null"/> if none exists.</summary>
    protected abstract Task<TSnapshot?> LoadLocalAsync(TKey key, CancellationToken cancellationToken);

    /// <summary>Compares a known-present local row against the remote snapshot for drift.</summary>
    protected abstract bool AreEqual(TSnapshot local, TSnapshot remote);

    /// <summary>
    /// Creates the local row from the remote snapshot. <paramref name="observedAtUtc"/> is fixed
    /// for the whole sweep run (see <see cref="ProjectionSweepClock"/>) so implementations can
    /// stamp it as the row's "as of" timestamp without each key racing its own wall-clock read.
    /// </summary>
    protected abstract Task CreateAsync(TKey key, TSnapshot remote, DateTimeOffset observedAtUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the local row to match the remote snapshot. Implementations must respect their own
    /// staleness guard (e.g. "never roll back state a newer event already applied") using
    /// <paramref name="observedAtUtc"/> — this base class does not enforce that guard itself.
    /// </summary>
    protected abstract Task UpdateAsync(TKey key, TSnapshot local, TSnapshot remote, DateTimeOffset observedAtUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Identifies "the write failed because of a concurrent duplicate insert" by runtime type name.
    /// Override to recognize a different persistence provider's exception type name; the default
    /// matches EF Core's <c>DbUpdateException</c> without referencing the type itself.
    /// </summary>
    protected virtual bool IsDuplicateInsertFailure(Exception exception) =>
        exception.GetType().Name == "DbUpdateException";

    public async Task<ProjectionReconcileOutcome> ReconcileAsync(TKey key, CancellationToken cancellationToken)
    {
        var observedAtUtc = ProjectionSweepClock.ObservedAtUtc ?? _timeProvider.GetUtcNow();

        var fetch = await FetchAsync(key, cancellationToken).ConfigureAwait(false);
        if (fetch.Status == ProjectionFetchStatus.Unavailable)
            return ProjectionReconcileOutcome.SourceUnavailable;

        if (fetch.Status == ProjectionFetchStatus.NotFound)
            return ProjectionReconcileOutcome.SourceMissing;

        var remote = fetch.Snapshot!;
        var local = await LoadLocalAsync(key, cancellationToken).ConfigureAwait(false);

        if (local is null)
            return await CreateOrRecoverFromRaceAsync(key, remote, observedAtUtc, cancellationToken).ConfigureAwait(false);

        if (AreEqual(local, remote))
            return ProjectionReconcileOutcome.Unchanged;

        await UpdateAsync(key, local, remote, observedAtUtc, cancellationToken).ConfigureAwait(false);
        return ProjectionReconcileOutcome.Updated;
    }

    private async Task<ProjectionReconcileOutcome> CreateOrRecoverFromRaceAsync(
        TKey key, TSnapshot remote, DateTimeOffset observedAtUtc, CancellationToken cancellationToken)
    {
        try
        {
            await CreateAsync(key, remote, observedAtUtc, cancellationToken).ConfigureAwait(false);
            return ProjectionReconcileOutcome.Created;
        }
        catch (Exception ex) when (IsDuplicateInsertFailure(ex))
        {
            // A save failure alone does not mean "a concurrent writer already created the row" —
            // verify the post-condition before treating it as benign (ProvisionFirstPartyListingCommandHandler
            // precedent). Anything that is not a genuine collision rethrows; the caller (the sweep
            // loop, or a read-through caller) decides what to do with an unexpected failure.
            var postCondition = await LoadLocalAsync(key, cancellationToken).ConfigureAwait(false);
            if (postCondition is null)
                throw;

            _logger.LogWarning(
                ex,
                "Duplicate-insert race reconciling {Key}: a concurrent writer already created the row; " +
                "treating this create as a benign no-op.",
                key);

            return ProjectionReconcileOutcome.Unchanged;
        }
    }
}
