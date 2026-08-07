using Microsoft.Extensions.Logging;
using MW.Projections.Reconcile;

namespace MW.Projections.Tests.TestSupport;

/// <summary>
/// Test double for <see cref="ProjectionReconcilerBase{TKey,TSnapshot}"/> that delegates each
/// abstract member to a settable <see cref="Func{T,TResult}"/>, and records every
/// <c>observedAtUtc</c> it is given via <see cref="CreateAsync"/>/<see cref="UpdateAsync"/> so
/// tests can assert a sweep run held it fixed across batches.
/// </summary>
internal sealed class RecordingReconciler : ProjectionReconcilerBase<string, TestSnapshot>
{
    public RecordingReconciler(TimeProvider timeProvider, ILogger logger) : base(timeProvider, logger)
    {
    }

    public Func<string, CancellationToken, Task<ProjectionFetchResult<TestSnapshot>>> FetchImpl { get; set; } =
        (_, _) => Task.FromResult(ProjectionFetchResult<TestSnapshot>.NotFound());

    public Func<string, CancellationToken, Task<TestSnapshot?>> LoadLocalImpl { get; set; } =
        (_, _) => Task.FromResult<TestSnapshot?>(null);

    public Func<TestSnapshot, TestSnapshot, bool> AreEqualImpl { get; set; } = (local, remote) => local == remote;

    public Func<string, TestSnapshot, DateTimeOffset, CancellationToken, Task>? CreateImpl { get; set; }

    public Func<string, TestSnapshot, TestSnapshot, DateTimeOffset, CancellationToken, Task>? UpdateImpl { get; set; }

    public List<DateTimeOffset> ObservedTimestamps { get; } = new();

    protected override Task<ProjectionFetchResult<TestSnapshot>> FetchAsync(string key, CancellationToken cancellationToken) =>
        FetchImpl(key, cancellationToken);

    protected override Task<TestSnapshot?> LoadLocalAsync(string key, CancellationToken cancellationToken) =>
        LoadLocalImpl(key, cancellationToken);

    protected override bool AreEqual(TestSnapshot local, TestSnapshot remote) => AreEqualImpl(local, remote);

    protected override async Task CreateAsync(
        string key, TestSnapshot remote, DateTimeOffset observedAtUtc, CancellationToken cancellationToken)
    {
        ObservedTimestamps.Add(observedAtUtc);
        if (CreateImpl is not null)
            await CreateImpl(key, remote, observedAtUtc, cancellationToken);
    }

    protected override async Task UpdateAsync(
        string key, TestSnapshot local, TestSnapshot remote, DateTimeOffset observedAtUtc, CancellationToken cancellationToken)
    {
        ObservedTimestamps.Add(observedAtUtc);
        if (UpdateImpl is not null)
            await UpdateImpl(key, local, remote, observedAtUtc, cancellationToken);
    }
}
