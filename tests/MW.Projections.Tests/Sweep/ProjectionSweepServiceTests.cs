using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MW.Projections.Reconcile;
using MW.Projections.Sweep;
using MW.Projections.Tests.TestSupport;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace MW.Projections.Tests.Sweep;

public class ProjectionSweepServiceTests
{
    [Fact]
    public async Task ExecuteAsync_Should_DoNoWork_WhenDisabled()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var service = new ProjectionSweepService<string>(
            scopeFactory.Object,
            MsOptions.Create(new ProjectionSweepOptions { Enabled = false }),
            TimeProvider.System,
            NullLogger<ProjectionSweepService<string>>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        scopeFactory.Verify(f => f.CreateScope(), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_Should_StopAfterMaxBatchesPerRun()
    {
        const int batchSize = 5;
        const int maxBatches = 3;

        var source = new Mock<IProjectionSweepSource<string>>();
        var callCount = 0;
        source
            .Setup(s => s.GetNextBatchAsync(batchSize, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // Always returns a FULL batch, so without the MaxBatchesPerRun cap this would loop
                // forever (mirrors SyncListingReferencesCommand's MaxPages safety-cap precedent).
                return (IReadOnlyList<string>)Enumerable.Range(0, batchSize)
                    .Select(i => $"batch{callCount}-key{i}")
                    .ToList();
            });

        var reconciler = new Mock<IProjectionReconciler<string>>();
        reconciler
            .Setup(r => r.ReconcileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectionReconcileOutcome.Unchanged);

        var service = CreateService(
            source.Object, reconciler.Object,
            new ProjectionSweepOptions { Enabled = true, BatchSize = batchSize, MaxBatchesPerRun = maxBatches });

        var batchesProcessed = await service.RunOnceAsync(CancellationToken.None);

        batchesProcessed.Should().Be(maxBatches);
        source.Verify(
            s => s.GetNextBatchAsync(batchSize, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(maxBatches));
    }

    [Fact]
    public async Task RunOnceAsync_Should_StopEarly_WhenABatchIsShorterThanBatchSize()
    {
        const int batchSize = 5;

        var source = new Mock<IProjectionSweepSource<string>>();
        source
            .SetupSequence(s => s.GetNextBatchAsync(batchSize, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)new[] { "k1", "k2" }); // short page ⇒ end of keyspace

        var reconciler = new Mock<IProjectionReconciler<string>>();
        reconciler
            .Setup(r => r.ReconcileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectionReconcileOutcome.Unchanged);

        var service = CreateService(
            source.Object, reconciler.Object,
            new ProjectionSweepOptions { Enabled = true, BatchSize = batchSize, MaxBatchesPerRun = 50 });

        var batchesProcessed = await service.RunOnceAsync(CancellationToken.None);

        batchesProcessed.Should().Be(1);
        source.Verify(
            s => s.GetNextBatchAsync(batchSize, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_Should_ContinueToNextKey_WhenAReconcilerCallThrows()
    {
        var source = new Mock<IProjectionSweepSource<string>>();
        source
            .SetupSequence(s => s.GetNextBatchAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)new[] { "bad-key", "good-key" });

        var reconciler = new Mock<IProjectionReconciler<string>>();
        reconciler
            .Setup(r => r.ReconcileAsync("bad-key", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        reconciler
            .Setup(r => r.ReconcileAsync("good-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectionReconcileOutcome.Unchanged);

        var service = CreateService(
            source.Object, reconciler.Object,
            new ProjectionSweepOptions { Enabled = true, BatchSize = 200, MaxBatchesPerRun = 50 });

        var act = () => service.RunOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        reconciler.Verify(r => r.ReconcileAsync("bad-key", It.IsAny<CancellationToken>()), Times.Once);
        reconciler.Verify(r => r.ReconcileAsync("good-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_Should_UseOneTimestamp_ForTheWholeRun_EvenAsTheClockAdvancesBetweenBatches()
    {
        const int batchSize = 1;
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-07T00:00:00Z"));

        var source = new Mock<IProjectionSweepSource<string>>();
        var callCount = 0;
        source
            .Setup(s => s.GetNextBatchAsync(batchSize, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount > 2)
                    return Array.Empty<string>();

                // Simulate real wall-clock movement happening WHILE the run is in progress.
                timeProvider.Advance(TimeSpan.FromMinutes(5));
                return new[] { $"key{callCount}" };
            });

        var reconciler = new RecordingReconciler(TimeProvider.System, NullLogger.Instance)
        {
            FetchImpl = (_, _) => Task.FromResult(ProjectionFetchResult<TestSnapshot>.Found(new TestSnapshot("v"))),
            LoadLocalImpl = (_, _) => Task.FromResult<TestSnapshot?>(null)
        };

        var services = new ServiceCollection();
        services.AddSingleton(source.Object);
        services.AddSingleton<IProjectionReconciler<string>>(reconciler);
        var provider = services.BuildServiceProvider();

        var service = new ProjectionSweepService<string>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            MsOptions.Create(new ProjectionSweepOptions { Enabled = true, BatchSize = batchSize, MaxBatchesPerRun = 50 }),
            timeProvider,
            NullLogger<ProjectionSweepService<string>>.Instance);

        await service.RunOnceAsync(CancellationToken.None);

        reconciler.ObservedTimestamps.Should().HaveCount(2);
        reconciler.ObservedTimestamps.Distinct().Should().ContainSingle(
            "the whole sweep run must observe ONE fixed instant even though the wall clock advanced between batches");
    }

    private static ProjectionSweepService<string> CreateService(
        IProjectionSweepSource<string> source,
        IProjectionReconciler<string> reconciler,
        ProjectionSweepOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(source);
        services.AddSingleton(reconciler);
        var provider = services.BuildServiceProvider();

        return new ProjectionSweepService<string>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            MsOptions.Create(options),
            TimeProvider.System,
            NullLogger<ProjectionSweepService<string>>.Instance);
    }
}
