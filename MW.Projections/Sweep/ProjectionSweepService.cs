using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MW.Projections.Reconcile;

namespace MW.Projections.Sweep;

/// <summary>
/// Periodic background pass over <see cref="IProjectionSweepSource{TKey}"/>, reconciling every key
/// via <see cref="IProjectionReconciler{TKey}"/>. Registered per <typeparamref name="TKey"/> via
/// <see cref="Extensions.ProjectionsServiceCollectionExtensions.AddProjectionSweep{TKey,TSource,TReconciler}"/>.
/// <para>
/// A <see cref="BackgroundService"/> is a singleton, but <see cref="IProjectionSweepSource{TKey}"/>
/// and <see cref="IProjectionReconciler{TKey}"/> are registered scoped (a consuming service's
/// reconciler will typically hold a scoped <c>DbContext</c>) — so this service takes an
/// <see cref="IServiceScopeFactory"/> and resolves both from a fresh <see cref="IServiceScope"/>
/// once per run, same shape as ASP.NET Core's documented timed-background-task pattern.
/// </para>
/// </summary>
public sealed class ProjectionSweepService<TKey> : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ProjectionSweepOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProjectionSweepService<TKey>> _logger;

    /// <summary>
    /// Cursor left off by the previous run, persisted across <see cref="RunOnceAsync"/> calls on
    /// this singleton-lifetime instance. Safe as plain instance state (no synchronization) because
    /// <see cref="ExecuteAsync"/> always fully awaits one run before starting the next — there is
    /// never a concurrent <see cref="RunOnceAsync"/> call against the same instance. Reset to
    /// <see langword="default"/> once a run reaches the end of the keyspace (an empty or
    /// short/partial final page), so the sweep wraps around and starts over; otherwise carried
    /// forward so a keyspace larger than <c>MaxBatchesPerRun * BatchSize</c> is fully covered over
    /// multiple runs instead of the same first slice being reprocessed forever.
    /// </summary>
    private TKey? _cursor;

    public ProjectionSweepService(
        IServiceScopeFactory scopeFactory,
        IOptions<ProjectionSweepOptions> options,
        TimeProvider timeProvider,
        ILogger<ProjectionSweepService<TKey>> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation(
                "Projection sweep for {KeyType} is disabled (ProjectionSweepOptions.Enabled=false); " +
                "the hosted service will not run.",
                typeof(TKey).Name);
            return;
        }

        using var timer = new PeriodicTimer(_options.Value.Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A whole-run failure (e.g. the sweep source itself is unreachable) must not kill
                // the hosted service — log and try again on the next interval.
                _logger.LogError(ex, "Projection sweep run for {KeyType} failed unexpectedly.", typeof(TKey).Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Runs one sweep to completion (or until <see cref="ProjectionSweepOptions.MaxBatchesPerRun"/>
    /// is hit): resumes from the cursor <see cref="_cursor"/> the previous run left off at, pages
    /// via <see cref="IProjectionSweepSource{TKey}"/>, reconciling each key under ONE fixed
    /// <c>observedAtUtc</c> (see <see cref="ProjectionSweepClock"/>). A single key's reconcile
    /// failure is logged and does not abort the rest of the run; a genuine cancellation of
    /// <paramref name="cancellationToken"/> propagates immediately instead (see the inner
    /// <c>catch</c> below). Returns the number of batches processed — <c>internal</c> so tests can
    /// drive a run deterministically without going through <see cref="PeriodicTimer"/>.
    /// </summary>
    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var observedAtUtc = _timeProvider.GetUtcNow();

        using var clockScope = ProjectionSweepClock.BeginRun(observedAtUtc);
        using var scope = _scopeFactory.CreateScope();

        var source = scope.ServiceProvider.GetRequiredService<IProjectionSweepSource<TKey>>();
        var reconciler = scope.ServiceProvider.GetRequiredService<IProjectionReconciler<TKey>>();

        var cursor = _cursor;
        var batchesProcessed = 0;
        var reachedEndOfKeyspace = false;

        try
        {
            while (batchesProcessed < options.MaxBatchesPerRun)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = await source.GetNextBatchAsync(options.BatchSize, cursor, cancellationToken)
                    .ConfigureAwait(false);
                batchesProcessed++;

                if (batch.Count == 0)
                {
                    reachedEndOfKeyspace = true;
                    break;
                }

                foreach (var key in batch)
                {
                    try
                    {
                        var outcome = await reconciler.ReconcileAsync(key, cancellationToken).ConfigureAwait(false);
                        _logger.LogDebug("Projection sweep reconciled {Key}: {Outcome}.", key, outcome);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // A genuine cancellation of THIS run's token (graceful shutdown) must stop
                        // promptly, not be treated as an ordinary per-key reconcile failure — the
                        // outer while-loop's own ThrowIfCancellationRequested only re-checks at the
                        // START of the next batch, so without this the remaining keys in the
                        // CURRENT batch would still be churned through and logged as fake errors.
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex, "Projection sweep failed to reconcile {Key}; continuing with the next key.", key);
                    }
                }

                cursor = batch[^1];

                if (batch.Count < options.BatchSize)
                {
                    reachedEndOfKeyspace = true;
                    break;
                }
            }

            if (!reachedEndOfKeyspace && batchesProcessed >= options.MaxBatchesPerRun)
            {
                _logger.LogWarning(
                    "Projection sweep for {KeyType} hit MaxBatchesPerRun={MaxBatchesPerRun} without reaching " +
                    "the end of the keyspace; resuming from cursor {Cursor} on the next run.",
                    typeof(TKey).Name, options.MaxBatchesPerRun, cursor);
            }
        }
        finally
        {
            // Persist progress even when this run ended via a propagating cancellation: whatever
            // batches fully completed before the cancellation still advanced the cursor, so the
            // next run resumes from there instead of re-walking from the very start. Only wrap
            // around to the beginning of the keyspace on a genuine end-of-keyspace signal.
            _cursor = reachedEndOfKeyspace ? default : cursor;
        }

        return batchesProcessed;
    }
}
