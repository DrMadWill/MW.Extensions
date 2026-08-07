using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MW.Projections.Repair;

/// <summary>
/// Default <see cref="IProjectionRepairRequester"/>. Publishes through MassTransit's
/// <see cref="IPublishEndpoint"/> directly rather than <c>MW.Messaging.Abstractions</c>'
/// <c>IIntegrationEventPublisher</c> — see the Task 1 report for why. Publishing through
/// <see cref="IPublishEndpoint"/> resolved from the caller's own scope (an HTTP request or a
/// consumer) means the service's existing transactional outbox (configured via
/// <c>MW.Messaging.MassTransit</c>'s <c>UseEntityFrameworkOutbox</c>/<c>UseEntityFrameworkInboxOutbox</c>)
/// applies to this publish exactly as it does to any other, with no special-casing here.
/// </summary>
internal sealed class ProjectionRepairRequester : IProjectionRepairRequester
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ProjectionRepairDedupCache _dedupCache;
    private readonly Options.ProjectionRepairOptions _options;
    private readonly ILogger<ProjectionRepairRequester> _logger;

    public ProjectionRepairRequester(
        IPublishEndpoint publishEndpoint,
        ProjectionRepairDedupCache dedupCache,
        IOptions<Options.ProjectionRepairOptions> options,
        ILogger<ProjectionRepairRequester> logger)
    {
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
        _dedupCache = dedupCache ?? throw new ArgumentNullException(nameof(dedupCache));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task RequestAsync(string referenceType, string referenceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceId);

        if (_dedupCache.ShouldSuppress(referenceType, referenceId, _options.DedupWindow))
        {
            _logger.LogDebug(
                "Suppressing duplicate projection repair request for {ReferenceType}/{ReferenceId}: " +
                "an identical request was already published within the {DedupWindow} dedup window.",
                referenceType, referenceId, _options.DedupWindow);
            return Task.CompletedTask;
        }

        return PublishAsync(referenceType, referenceId, cancellationToken);
    }

    private async Task PublishAsync(string referenceType, string referenceId, CancellationToken cancellationToken)
    {
        var message = new ProjectionRepairRequested(
            referenceType, referenceId, Guid.NewGuid(), DateTimeOffset.UtcNow);

        _logger.LogInformation(
            "Requesting projection repair for {ReferenceType}/{ReferenceId} (CorrelationId={CorrelationId}).",
            referenceType, referenceId, message.CorrelationId);

        await _publishEndpoint.Publish(message, cancellationToken);
    }
}
