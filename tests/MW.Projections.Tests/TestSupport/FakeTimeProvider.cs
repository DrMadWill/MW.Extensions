namespace MW.Projections.Tests.TestSupport;

/// <summary>
/// Minimal controllable <see cref="TimeProvider"/> so dedup-window tests can assert the exact
/// boundary (suppressed just inside the window, allowed just after) without a real wall-clock
/// wait.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset start) => _utcNow = start;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}
