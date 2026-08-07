using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MW.Projections.Repair;

namespace MW.Projections.Tests.Repair;

public class ProjectionRepairRequestedConsumerTests
{
    [Fact]
    public async Task Consume_Should_ThrowAndLog_ForUnknownReferenceType()
    {
        var registry = new Mock<IProjectionRepairSourceRegistry>();
        Type? unusedType = null;
        registry.Setup(r => r.TryGetSourceType("unknown", out unusedType)).Returns(false);

        var serviceProvider = new Mock<IServiceProvider>();
        var logger = new Mock<ILogger<ProjectionRepairRequestedConsumer>>();
        var consumer = new ProjectionRepairRequestedConsumer(registry.Object, serviceProvider.Object, logger.Object);

        var message = new ProjectionRepairRequested("unknown", "1", Guid.NewGuid(), DateTimeOffset.UtcNow);
        var context = BuildContext(message, CancellationToken.None);

        var act = () => consumer.Consume(context);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*unknown*");

        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        serviceProvider.Invocations.Should().BeEmpty("an unresolvable source must not be looked up at all");
    }

    [Fact]
    public async Task Consume_Should_ResolveSourceFromServiceProvider_ByRegisteredType()
    {
        var sourceType = typeof(FakeSource);
        var registry = new Mock<IProjectionRepairSourceRegistry>();
        registry.Setup(r => r.TryGetSourceType("product", out sourceType)).Returns(true);

        var source = new Mock<IProjectionRepairSource>();
        source.Setup(s => s.RepairAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(FakeSource))).Returns(source.Object);

        var consumer = new ProjectionRepairRequestedConsumer(
            registry.Object, serviceProvider.Object, NullLogger<ProjectionRepairRequestedConsumer>.Instance);

        var message = new ProjectionRepairRequested("product", "abc-1", Guid.NewGuid(), DateTimeOffset.UtcNow);
        var context = BuildContext(message, CancellationToken.None);

        await consumer.Consume(context);

        source.Verify(s => s.RepairAsync("abc-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_Should_ThreadTheConsumeContextCancellationToken()
    {
        var sourceType = typeof(FakeSource);
        var registry = new Mock<IProjectionRepairSourceRegistry>();
        registry.Setup(r => r.TryGetSourceType("product", out sourceType)).Returns(true);

        var source = new Mock<IProjectionRepairSource>();
        source.Setup(s => s.RepairAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(FakeSource))).Returns(source.Object);

        var consumer = new ProjectionRepairRequestedConsumer(
            registry.Object, serviceProvider.Object, NullLogger<ProjectionRepairRequestedConsumer>.Instance);

        using var cts = new CancellationTokenSource();
        var message = new ProjectionRepairRequested("product", "abc-1", Guid.NewGuid(), DateTimeOffset.UtcNow);
        var context = BuildContext(message, cts.Token);

        await consumer.Consume(context);

        source.Verify(s => s.RepairAsync("abc-1", cts.Token), Times.Once);
    }

    private static ConsumeContext<ProjectionRepairRequested> BuildContext(
        ProjectionRepairRequested message, CancellationToken cancellationToken)
    {
        var context = new Mock<ConsumeContext<ProjectionRepairRequested>>();
        context.Setup(c => c.Message).Returns(message);
        context.Setup(c => c.CancellationToken).Returns(cancellationToken);
        return context.Object;
    }

    private sealed class FakeSource : IProjectionRepairSource
    {
        public Task RepairAsync(string referenceId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
