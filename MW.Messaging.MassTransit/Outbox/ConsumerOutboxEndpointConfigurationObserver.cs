using MassTransit;

namespace MW.Messaging.MassTransit.Outbox;

/// <summary>
/// Applies the Entity Framework consumer outbox to every receive endpoint configured via
/// <c>ConfigureEndpoints</c>. Without this endpoint-level filter, messages published from inside
/// a consumer go straight to the broker through the <see cref="ConsumeContext"/> and race the
/// consumer's own database transaction — the bus outbox (<c>UseBusOutbox</c>) only covers
/// publishes made outside a consumer scope. Endpoints that host no consumers are unaffected
/// (the filter is a per-consumer specification and becomes a no-op there).
/// </summary>
internal sealed class ConsumerOutboxEndpointConfigurationObserver : IEndpointConfigurationObserver
{
    private readonly IBusRegistrationContext _context;
    private readonly Action<IBusRegistrationContext, IReceiveEndpointConfigurator> _configureEndpoint;

    public ConsumerOutboxEndpointConfigurationObserver(
        IBusRegistrationContext context,
        Action<IBusRegistrationContext, IReceiveEndpointConfigurator> configureEndpoint)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _configureEndpoint = configureEndpoint ?? throw new ArgumentNullException(nameof(configureEndpoint));
    }

    public void EndpointConfigured<T>(T configurator)
        where T : IReceiveEndpointConfigurator
        => _configureEndpoint(_context, configurator);
}
