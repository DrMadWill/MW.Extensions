using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MW.Messaging.MassTransit.Extensions;

namespace MW.Messaging.MassTransit.Tests.Options;

/// <summary>
/// Guards the 1.0.5 contract fix: <c>UseEntityFrameworkInboxOutbox</c> must wire the
/// consumer-side outbox onto receive endpoints, not just the bus outbox. Before 1.0.5 the
/// method configured the bus outbox only, so publishes from inside a consumer bypassed the
/// outbox via the ConsumeContext and raced the consumer's database commit (observed as
/// notification-service DISP-001: events arriving before the row they reference).
/// </summary>
public class ConsumerOutboxWiringTests
{
    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);

    [Fact]
    public void UseEntityFrameworkInboxOutbox_Should_SetConsumerOutboxEndpointConfigurator()
    {
        var options = new MassTransitMessagingOptions();

        options.UseEntityFrameworkInboxOutbox<TestDbContext>();

        options.OutboxConfigurator.Should().NotBeNull();
        options.ConsumerOutboxEndpointConfigurator.Should().NotBeNull(
            "consumer publishes bypass the bus outbox via the ConsumeContext, so the endpoint-level filter is the whole point of this method");
    }

    [Fact]
    public void UseEntityFrameworkOutbox_Should_NotTouchConsumerOutbox()
    {
        var options = new MassTransitMessagingOptions();

        options.UseEntityFrameworkOutbox<TestDbContext>();

        options.OutboxConfigurator.Should().NotBeNull();
        options.ConsumerOutboxEndpointConfigurator.Should().BeNull(
            "bus-outbox-only behaviour must stay unchanged for existing services that opted into UseEntityFrameworkOutbox");
    }
}
