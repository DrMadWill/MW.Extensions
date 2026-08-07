namespace MW.Projections.Sweep;

/// <summary>
/// Ambient, async-flowing "current sweep run" instant. <see cref="ProjectionSweepService{TKey}"/>
/// reads its own <see cref="TimeProvider"/> exactly ONCE per run and opens a scope here that wraps
/// every batch/key reconciled during that run; <c>ProjectionReconcilerBase&lt;TKey,TSnapshot&gt;</c>
/// checks this ambient value first and falls back to its own <see cref="TimeProvider"/> only when
/// no sweep run is in progress (e.g. a read-through reconcile triggered by an admin page load).
/// <para>
/// This is what makes "ONE <c>observedAtUtc</c> for the whole sweep run" hold even though the
/// public <c>IProjectionReconciler&lt;TKey&gt;.ReconcileAsync(key, ct)</c> contract has no
/// timestamp parameter (Phase 3 implements against that exact signature — it cannot change) and
/// even though a real wall clock legitimately advances between batches. Mirrors
/// <c>SyncListingReferencesCommand</c>'s single <c>observedAtUtc = DateTimeOffset.UtcNow</c> fixed
/// before the paging loop starts.
/// </para>
/// <c>AsyncLocal&lt;T&gt;</c> flows through <c>await</c> continuations on the same logical call
/// chain, which is exactly the sequential "await each key, then the next" shape both the sweep
/// loop and a single reconcile call use — no ambient value leaks across unrelated concurrent runs.
/// </summary>
internal static class ProjectionSweepClock
{
    private static readonly AsyncLocal<DateTimeOffset?> Ambient = new();

    public static DateTimeOffset? ObservedAtUtc => Ambient.Value;

    public static IDisposable BeginRun(DateTimeOffset observedAtUtc)
    {
        Ambient.Value = observedAtUtc;
        return new RunScope();
    }

    private sealed class RunScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Ambient.Value = null;
        }
    }
}
