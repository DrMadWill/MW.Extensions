using System.Collections.Concurrent;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using MW.Projections.Extensions;
using MW.Projections.Repair;

namespace MW.Projections.Tests.Harness;

/// <summary>
/// End-to-end proof (real MassTransit in-memory transport + real DI, no mocks) that
/// <see cref="ProjectionRepairRequestedConsumer"/> resolves <see cref="IProjectionRepairSource"/>
/// from the message's own DI scope: a source that captures a fresh scoped id per construction
/// must see a different id for each of two consumed messages, proving the container is not
/// handing back one long-lived instance the way the ported prior art's startup-captured
/// delegate did.
/// </summary>
public class ProjectionRepairHarnessTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ConcurrentBag<Guid>>();
        services.AddProjectionRepairSource<ScopedInstanceCapturingSource>("widget");

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<ProjectionRepairRequestedConsumer>();
            cfg.UsingInMemory((context, busCfg) => busCfg.ConfigureEndpoints(context));
        });

        _provider = services.BuildServiceProvider(true);
        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task Consume_Should_ResolveAFreshSourceInstance_PerMessage()
    {
        using var scope = _provider.CreateScope();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await publishEndpoint.Publish(new ProjectionRepairRequested("widget", "1", Guid.NewGuid(), DateTimeOffset.UtcNow));
        await publishEndpoint.Publish(new ProjectionRepairRequested("widget", "2", Guid.NewGuid(), DateTimeOffset.UtcNow));

        var observedInstances = _provider.GetRequiredService<ConcurrentBag<Guid>>();
        await WaitUntilAsync(() => observedInstances.Count >= 2, TimeSpan.FromSeconds(5));

        observedInstances.Distinct().Should().HaveCount(2,
            "each consumed message must be handled by its own DI scope, so a scoped source " +
            "must be a fresh instance every time — not one instance captured once at startup");
    }

    [Fact]
    public async Task Consume_Should_FaultForAnUnregisteredReferenceType()
    {
        using var scope = _provider.CreateScope();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await publishEndpoint.Publish(new ProjectionRepairRequested("unregistered-type", "1", Guid.NewGuid(), DateTimeOffset.UtcNow));

        (await _harness.Consumed.Any<ProjectionRepairRequested>()).Should().BeTrue();

        var consumerHarness = _harness.GetConsumerHarness<ProjectionRepairRequestedConsumer>();
        await WaitUntilAsync(
            () => consumerHarness.Consumed.Select<ProjectionRepairRequested>().Any(),
            TimeSpan.FromSeconds(5));

        (await _harness.Published.Any<Fault<ProjectionRepairRequested>>())
            .Should().BeTrue("an unknown reference type must fault, not silently succeed");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Condition was not met within the timeout.");
    }

    private sealed class ScopedInstanceCapturingSource : IProjectionRepairSource
    {
        private readonly ConcurrentBag<Guid> _observedInstances;
        private readonly Guid _instanceId = Guid.NewGuid();

        public ScopedInstanceCapturingSource(ConcurrentBag<Guid> observedInstances)
        {
            _observedInstances = observedInstances;
        }

        public Task RepairAsync(string referenceId, CancellationToken cancellationToken)
        {
            _observedInstances.Add(_instanceId);
            return Task.CompletedTask;
        }
    }
}
