using FluentAssertions;
using MW.Projections.Repair;

namespace MW.Projections.Tests.Repair;

public class ProjectionRepairSourceRegistryTests
{
    [Fact]
    public void TryGetSourceType_Should_ReturnFalse_ForUnknownReferenceType()
    {
        var registry = new ProjectionRepairSourceRegistry(new Dictionary<string, Type>());

        var found = registry.TryGetSourceType("unknown", out var sourceType);

        found.Should().BeFalse();
        sourceType.Should().BeNull();
    }

    [Fact]
    public void TryGetSourceType_Should_ReturnRegisteredType()
    {
        var registry = new ProjectionRepairSourceRegistry(
            new Dictionary<string, Type> { ["product"] = typeof(FakeSource) });

        var found = registry.TryGetSourceType("product", out var sourceType);

        found.Should().BeTrue();
        sourceType.Should().Be(typeof(FakeSource));
    }

    [Fact]
    public void TryGetSourceType_Should_BeCaseSensitive()
    {
        var registry = new ProjectionRepairSourceRegistry(
            new Dictionary<string, Type> { ["product"] = typeof(FakeSource) });

        registry.TryGetSourceType("Product", out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetSourceType_Should_BeSafeForConcurrentReads()
    {
        var sources = Enumerable.Range(0, 200)
            .ToDictionary(i => $"type-{i}", _ => typeof(FakeSource));
        var registry = new ProjectionRepairSourceRegistry(sources);

        var failures = 0;

        Parallel.For(0, 5000, i =>
        {
            var key = $"type-{i % 200}";
            if (!registry.TryGetSourceType(key, out var sourceType) || sourceType != typeof(FakeSource))
            {
                Interlocked.Increment(ref failures);
            }
        });

        failures.Should().Be(0);
    }

    private sealed class FakeSource : IProjectionRepairSource
    {
        public Task RepairAsync(string referenceId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
