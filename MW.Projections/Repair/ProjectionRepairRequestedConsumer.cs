using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MW.Projections.Repair;

/// <summary>
/// Owner-side MassTransit consumer for <see cref="ProjectionRepairRequested"/>. The owning
/// service registers this via its own MassTransit setup (e.g.
/// <c>busConfig.AddConsumer&lt;ProjectionRepairRequestedConsumer&gt;()</c>) after calling
/// <c>services.AddProjectionRepairSource&lt;TSource&gt;(referenceType)</c> for each type it owns.
/// </summary>
/// <remarks>
/// The <see cref="IServiceProvider"/> injected here is resolved **from the message scope**:
/// MassTransit's DI container integration creates a fresh <see cref="IServiceScope"/> per
/// consumed message and constructs the consumer (and everything it depends on, including
/// <see cref="IServiceProvider"/> itself) from that scope. This is what makes a scoped
/// <see cref="IProjectionRepairSource"/> — one that holds a scoped <c>DbContext</c> — safe to
/// resolve here, unlike the prior art's <c>Func&lt;string,Task&gt;</c> delegate captured once at
/// startup with no scope of its own.
/// </remarks>
public sealed class ProjectionRepairRequestedConsumer : IConsumer<ProjectionRepairRequested>
{
    private readonly IProjectionRepairSourceRegistry _registry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProjectionRepairRequestedConsumer> _logger;

    public ProjectionRepairRequestedConsumer(
        IProjectionRepairSourceRegistry registry,
        IServiceProvider serviceProvider,
        ILogger<ProjectionRepairRequestedConsumer> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<ProjectionRepairRequested> context)
    {
        var message = context.Message;

        if (!_registry.TryGetSourceType(message.ReferenceType, out var sourceType))
        {
            // Prior art silently returned here (RepairHandler.Consume: `if (eventName.HasEvent())`,
            // no else). A repair request for an unregistered type vanishing with no trace is exactly
            // the failure mode this channel exists to avoid — log AND throw so MassTransit's
            // retry/redelivery/fault pipeline makes the problem visible instead of swallowing it.
            _logger.LogError(
                "No projection repair source is registered for reference type {ReferenceType} " +
                "(ReferenceId={ReferenceId}, CorrelationId={CorrelationId}). The message will fault.",
                message.ReferenceType, message.ReferenceId, message.CorrelationId);

            throw new InvalidOperationException(
                $"No projection repair source is registered for reference type '{message.ReferenceType}'.");
        }

        var source = (IProjectionRepairSource)_serviceProvider.GetRequiredService(sourceType);

        _logger.LogInformation(
            "Repairing projection for {ReferenceType}/{ReferenceId} (CorrelationId={CorrelationId}).",
            message.ReferenceType, message.ReferenceId, message.CorrelationId);

        await source.RepairAsync(message.ReferenceId, context.CancellationToken);
    }
}
