using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MW.Projections.Options;
using MW.Projections.Repair;
using MW.Projections.Tests.TestSupport;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace MW.Projections.Tests.Repair;

public class ProjectionRepairRequesterTests
{
    [Fact]
    public async Task RequestAsync_Should_PublishExactlyOnce_WithNonEmptyCorrelationId()
    {
        ProjectionRepairRequested? published = null;
        var publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .Setup(p => p.Publish(It.IsAny<ProjectionRepairRequested>(), It.IsAny<CancellationToken>()))
            .Callback<ProjectionRepairRequested, CancellationToken>((message, _) => published = message)
            .Returns(Task.CompletedTask);

        var requester = CreateRequester(publishEndpoint.Object, new FakeTimeProvider(DateTimeOffset.UtcNow));

        await requester.RequestAsync("product", "abc-1", CancellationToken.None);

        publishEndpoint.Verify(
            p => p.Publish(It.IsAny<ProjectionRepairRequested>(), It.IsAny<CancellationToken>()),
            Times.Once);

        published.Should().NotBeNull();
        published!.ReferenceType.Should().Be("product");
        published.ReferenceId.Should().Be("abc-1");
        published.CorrelationId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task RequestAsync_Should_SuppressIdenticalRequest_InsideDedupWindow()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .Setup(p => p.Publish(It.IsAny<ProjectionRepairRequested>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var requester = CreateRequester(publishEndpoint.Object, timeProvider, dedupWindow: TimeSpan.FromSeconds(30));

        await requester.RequestAsync("product", "dup-1", CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await requester.RequestAsync("product", "dup-1", CancellationToken.None);

        publishEndpoint.Verify(
            p => p.Publish(It.IsAny<ProjectionRepairRequested>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestAsync_Should_AllowIdenticalRequest_AfterDedupWindowElapses()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .Setup(p => p.Publish(It.IsAny<ProjectionRepairRequested>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var requester = CreateRequester(publishEndpoint.Object, timeProvider, dedupWindow: TimeSpan.FromSeconds(30));

        await requester.RequestAsync("product", "dup-1", CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(31));
        await requester.RequestAsync("product", "dup-1", CancellationToken.None);

        publishEndpoint.Verify(
            p => p.Publish(It.IsAny<ProjectionRepairRequested>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RequestAsync_Should_NotSuppress_DifferentReferenceId()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .Setup(p => p.Publish(It.IsAny<ProjectionRepairRequested>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var requester = CreateRequester(publishEndpoint.Object, timeProvider);

        await requester.RequestAsync("product", "id-1", CancellationToken.None);
        await requester.RequestAsync("product", "id-2", CancellationToken.None);

        publishEndpoint.Verify(
            p => p.Publish(It.IsAny<ProjectionRepairRequested>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    private static ProjectionRepairRequester CreateRequester(
        IPublishEndpoint publishEndpoint, TimeProvider timeProvider, TimeSpan? dedupWindow = null)
    {
        var options = MsOptions.Create(new ProjectionRepairOptions
        {
            DedupWindow = dedupWindow ?? TimeSpan.FromSeconds(30)
        });

        return new ProjectionRepairRequester(
            publishEndpoint,
            new ProjectionRepairDedupCache(timeProvider),
            options,
            NullLogger<ProjectionRepairRequester>.Instance);
    }
}
