using System.Collections.Concurrent;

namespace MW.Projections.Repair;

/// <summary>
/// Small time-keyed concurrent set used to suppress a duplicate <c>(ReferenceType, ReferenceId)</c>
/// repair request inside <see cref="Options.ProjectionRepairOptions.DedupWindow"/>. Takes a
/// <see cref="TimeProvider"/> instead of reading <see cref="DateTimeOffset.UtcNow"/> directly so
/// the window boundary is deterministically testable.
/// </summary>
internal sealed class ProjectionRepairDedupCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _expiresAt = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public ProjectionRepairDedupCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Returns <see langword="true"/> if a request for the same key is already inside its dedup
    /// window (i.e. the caller should suppress publishing). Otherwise records the key with a new
    /// expiry and returns <see langword="false"/>. Atomic via <see cref="ConcurrentDictionary{TKey,TValue}.AddOrUpdate"/>
    /// so two concurrent callers for the same key cannot both slip through.
    /// </summary>
    public bool ShouldSuppress(string referenceType, string referenceId, TimeSpan window)
    {
        var key = BuildKey(referenceType, referenceId);
        var now = _timeProvider.GetUtcNow();
        var suppress = false;

        _expiresAt.AddOrUpdate(
            key,
            addValueFactory: _ => now + window,
            updateValueFactory: (_, currentExpiry) =>
            {
                if (currentExpiry > now)
                {
                    suppress = true;
                    return currentExpiry;
                }

                return now + window;
            });

        return suppress;
    }

    private static string BuildKey(string referenceType, string referenceId) => $"{referenceType}{referenceId}";
}
