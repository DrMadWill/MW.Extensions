using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MW.Projections.Reconcile;
using MW.Projections.Tests.TestSupport;

namespace MW.Projections.Tests.Reconcile;

public class ProjectionReconcilerBaseTests
{
    [Fact]
    public async Task ReconcileAsync_Should_ReturnSourceUnavailable_WhenFetchReportsUnavailable()
    {
        var reconciler = CreateReconciler();
        reconciler.FetchImpl = (_, _) => Task.FromResult(ProjectionFetchResult<TestSnapshot>.Unavailable());

        var outcome = await reconciler.ReconcileAsync("k1", CancellationToken.None);

        outcome.Should().Be(ProjectionReconcileOutcome.SourceUnavailable);
    }

    [Fact]
    public async Task ReconcileAsync_Should_ReturnSourceMissing_WhenFetchReportsNotFound()
    {
        var reconciler = CreateReconciler();
        reconciler.FetchImpl = (_, _) => Task.FromResult(ProjectionFetchResult<TestSnapshot>.NotFound());
        reconciler.LoadLocalImpl = (_, _) => Task.FromResult<TestSnapshot?>(new TestSnapshot("existing-local-row"));

        var outcome = await reconciler.ReconcileAsync("k1", CancellationToken.None);

        // The local row (if any) must be left untouched — SourceMissing must not imply deletion.
        outcome.Should().Be(ProjectionReconcileOutcome.SourceMissing);
    }

    [Fact]
    public async Task ReconcileAsync_Should_ReturnCreated_WhenLocalRowMissing()
    {
        var remote = new TestSnapshot("remote-value");
        var reconciler = CreateReconciler();
        reconciler.FetchImpl = (_, _) => Task.FromResult(ProjectionFetchResult<TestSnapshot>.Found(remote));
        reconciler.LoadLocalImpl = (_, _) => Task.FromResult<TestSnapshot?>(null);

        TestSnapshot? createdWith = null;
        reconciler.CreateImpl = (_, snapshot, _, _) =>
        {
            createdWith = snapshot;
            return Task.CompletedTask;
        };

        var outcome = await reconciler.ReconcileAsync("k1", CancellationToken.None);

        outcome.Should().Be(ProjectionReconcileOutcome.Created);
        createdWith.Should().Be(remote);
    }

    [Fact]
    public async Task ReconcileAsync_Should_ReturnUnchanged_WhenLocalRowAlreadyMatchesRemote()
    {
        var snapshot = new TestSnapshot("same-value");
        var reconciler = CreateReconciler();
        reconciler.FetchImpl = (_, _) => Task.FromResult(ProjectionFetchResult<TestSnapshot>.Found(snapshot));
        reconciler.LoadLocalImpl = (_, _) => Task.FromResult<TestSnapshot?>(snapshot with { });
        reconciler.AreEqualImpl = (local, remote) => local == remote;

        var updateCalled = false;
        reconciler.UpdateImpl = (_, _, _, _, _) =>
        {
            updateCalled = true;
            return Task.CompletedTask;
        };

        var outcome = await reconciler.ReconcileAsync("k1", CancellationToken.None);

        outcome.Should().Be(ProjectionReconcileOutcome.Unchanged);
        updateCalled.Should().BeFalse("an unchanged row must not be written");
    }

    [Fact]
    public async Task ReconcileAsync_Should_ReturnUpdated_WhenLocalRowDiffersFromRemote()
    {
        var local = new TestSnapshot("old-value");
        var remote = new TestSnapshot("new-value");
        var reconciler = CreateReconciler();
        reconciler.FetchImpl = (_, _) => Task.FromResult(ProjectionFetchResult<TestSnapshot>.Found(remote));
        reconciler.LoadLocalImpl = (_, _) => Task.FromResult<TestSnapshot?>(local);
        reconciler.AreEqualImpl = (l, r) => l == r;

        TestSnapshot? updatedFrom = null;
        TestSnapshot? updatedTo = null;
        reconciler.UpdateImpl = (_, l, r, _, _) =>
        {
            updatedFrom = l;
            updatedTo = r;
            return Task.CompletedTask;
        };

        var outcome = await reconciler.ReconcileAsync("k1", CancellationToken.None);

        outcome.Should().Be(ProjectionReconcileOutcome.Updated);
        updatedFrom.Should().Be(local);
        updatedTo.Should().Be(remote);
    }

    [Fact]
    public async Task ReconcileAsync_Should_ReturnUnchanged_WhenDuplicateInsertRaceLosesButPostConditionConfirmsRowExists()
    {
        var remote = new TestSnapshot("remote-value");
        var reconciler = CreateReconciler();
        reconciler.FetchImpl = (_, _) => Task.FromResult(ProjectionFetchResult<TestSnapshot>.Found(remote));

        var loadLocalCallCount = 0;
        reconciler.LoadLocalImpl = (_, _) =>
        {
            loadLocalCallCount++;
            // First call (before Create): row not there yet, so Create is attempted.
            // Second call (post-condition check after the caught exception): a concurrent writer
            // already created it.
            return Task.FromResult(loadLocalCallCount == 1 ? null : new TestSnapshot("concurrently-created"));
        };
        reconciler.CreateImpl = (_, _, _, _) => throw new DbUpdateException("unique constraint violated");

        var outcome = await reconciler.ReconcileAsync("k1", CancellationToken.None);

        outcome.Should().Be(ProjectionReconcileOutcome.Unchanged);
        loadLocalCallCount.Should().Be(2, "the post-condition must be verified, not assumed");
    }

    [Fact]
    public async Task ReconcileAsync_Should_Rethrow_WhenDuplicateInsertExceptionPostConditionCheckFails()
    {
        var remote = new TestSnapshot("remote-value");
        var reconciler = CreateReconciler();
        reconciler.FetchImpl = (_, _) => Task.FromResult(ProjectionFetchResult<TestSnapshot>.Found(remote));
        // Every LoadLocalAsync call (initial AND post-condition) returns null: the row genuinely
        // never got created, so the failure is real and must not be swallowed.
        reconciler.LoadLocalImpl = (_, _) => Task.FromResult<TestSnapshot?>(null);
        reconciler.CreateImpl = (_, _, _, _) => throw new DbUpdateException("connection dropped");

        var act = () => reconciler.ReconcileAsync("k1", CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ReconcileAsync_Should_NotCatch_ExceptionsThatAreNotDuplicateInsertFailures()
    {
        var remote = new TestSnapshot("remote-value");
        var reconciler = CreateReconciler();
        reconciler.FetchImpl = (_, _) => Task.FromResult(ProjectionFetchResult<TestSnapshot>.Found(remote));
        reconciler.LoadLocalImpl = (_, _) => Task.FromResult<TestSnapshot?>(null);
        reconciler.CreateImpl = (_, _, _, _) => throw new InvalidOperationException("unrelated failure");

        var act = () => reconciler.ReconcileAsync("k1", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static RecordingReconciler CreateReconciler() =>
        new(TimeProvider.System, NullLogger.Instance);
}
