using System.Diagnostics;
using System.Reflection;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MW.Messaging.Context;
using MW.Messaging.Identity;
using MW.Messaging.MassTransit.Context;
using MW.Messaging.MassTransit.Filters;
using MW.Messaging.MassTransit.Health;
using MW.Messaging.MassTransit.Identity;
using MW.Messaging.MassTransit.Naming;
using MW.Messaging.MassTransit.Observers;
using MW.Messaging.MassTransit.Options;
using MW.Messaging.Publishing;

namespace MW.Messaging.MassTransit.Extensions;

public static class MassTransitServiceCollectionExtensions
{
    public static IServiceCollection AddMassTransitMessaging(
        this IServiceCollection services,
        Action<MassTransitMessagingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MassTransitMessagingOptions();
        configure(options);

        // Register scoped message context accessor
        services.TryAddScoped<ScopedMessageContextAccessor>();
        services.TryAddScoped<IMessageContextAccessor>(sp => sp.GetRequiredService<ScopedMessageContextAccessor>());

        // Register IMessageExecutionContext mapped from consumer context
        services.TryAddScoped<IMessageExecutionContext, MassTransitMessageExecutionContext>();

        // Register header mapper
        services.TryAddSingleton<MW.Messaging.MassTransit.IMessageHeaderMapper, DefaultMessageHeaderMapper>();

        // Register publish context provider
        services.TryAddScoped<IPublishContextProvider, DefaultPublishContextProvider>();

        // Always register a service identity provider.
        // When ServiceName is configured, use the real value; otherwise register a safe
        // default that returns an empty identity so downstream code never sees null.
        services.TryAddSingleton<IServiceIdentityProvider>(
            _ => new ConfigurationServiceIdentityProvider(options.Options));

        // Register integration event publisher
        services.TryAddScoped<IIntegrationEventPublisher, Publishing.MassTransitIntegrationEventPublisher>();

        // Register MassTransit
        services.AddMassTransit(busConfig =>
        {
            // Consumer registration - assembly scanning
            if (options.ConsumerAssemblies.Count > 0)
            {
                foreach (var assembly in options.ConsumerAssemblies)
                {
                    busConfig.AddConsumers(assembly);
                }
            }

            // Custom consumer registration hook
            options.ConfigureConsumersAction?.Invoke(busConfig);

            // Endpoint naming
            var endpointFormatter = !string.IsNullOrWhiteSpace(options.Options.ServiceName)
                ? new ServiceEndpointNameFormatter(options.Options.ServiceName)
                : new ServiceEndpointNameFormatter(string.Empty);
            busConfig.SetEndpointNameFormatter(endpointFormatter);

            // Outbox configuration
            if (options.OutboxConfigurator != null)
            {
                options.OutboxConfigurator(busConfig);
            }

            // RabbitMQ transport
            busConfig.UsingRabbitMq((context, cfg) =>
            {
                var rabbitMqOptions = options.Options.RabbitMq;

                cfg.Host(rabbitMqOptions.Host, rabbitMqOptions.Port, rabbitMqOptions.VirtualHost, h =>
                {
                    h.Username(rabbitMqOptions.Username);
                    h.Password(rabbitMqOptions.Password);
                });

                // Retry policy
                var retryOptions = options.Options.Retry;
                cfg.UseMessageRetry(retryConfig =>
                {
                    if (retryOptions.RetryIntervalsInSeconds.Length > 0)
                    {
                        retryConfig.Intervals(
                            retryOptions.RetryIntervalsInSeconds
                                .Select(s => TimeSpan.FromSeconds(s))
                                .ToArray());
                    }
                    else
                    {
                        retryConfig.Interval(retryOptions.RetryCount, TimeSpan.FromSeconds(2));
                    }

                    // Exception type filtering
                    if (retryOptions.ExceptionTypeFilters is { Length: > 0 })
                    {
                        var logger = context.GetService<ILoggerFactory>()?.CreateLogger("MW.Messaging.MassTransit");
                        foreach (var typeName in retryOptions.ExceptionTypeFilters)
                        {
                            var exceptionType = Type.GetType(typeName);
                            if (exceptionType != null && typeof(Exception).IsAssignableFrom(exceptionType))
                            {
                                retryConfig.Handle(exceptionType);
                            }
                            else
                            {
                                logger?.LogWarning(
                                    "Retry exception type filter '{TypeName}' could not be resolved. Skipping.",
                                    typeName);
                            }
                        }
                    }
                });

                // Delayed redelivery policy
                var redeliveryOptions = options.Options.Redelivery;
                cfg.UseDelayedRedelivery(redeliveryConfig =>
                {
                    if (redeliveryOptions.RedeliveryIntervalsInSeconds.Length > 0)
                    {
                        redeliveryConfig.Intervals(
                            redeliveryOptions.RedeliveryIntervalsInSeconds
                                .Select(s => TimeSpan.FromSeconds(s))
                                .ToArray());
                    }
                    else
                    {
                        redeliveryConfig.Interval(redeliveryOptions.RedeliveryCount, TimeSpan.FromSeconds(15));
                    }
                });

                // Publish filter
                cfg.UsePublishFilter(typeof(HeaderEnrichmentPublishFilter<>), context);

                // Consume filter
                cfg.UseConsumeFilter(typeof(MessageContextConsumeFilter<>), context);

                // Observers
                cfg.ConnectPublishObserver(new MassTransitPublishObserverAdapter(
                    context.GetService<MW.Messaging.MassTransit.IPublishObserver>()));
                cfg.ConnectConsumeObserver(new MassTransitConsumeObserverAdapter(
                    context.GetService<MW.Messaging.MassTransit.IConsumeObserver>()));
                cfg.ConnectSendObserver(new MassTransitSendObserverAdapter(
                    context.GetService<MW.Messaging.MassTransit.ISendObserver>()));
                cfg.ConnectBusObserver(new BusLifecycleObserver(
                    context.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BusLifecycleObserver>>()));

                // Consumer-side outbox: apply the EF outbox consume filter to every receive
                // endpoint (set by UseEntityFrameworkInboxOutbox). Without it, publishes from
                // inside a consumer bypass the outbox via the ConsumeContext and race the
                // consumer's own database transaction. Must be connected BEFORE ConfigureEndpoints.
                if (options.ConsumerOutboxEndpointConfigurator is not null)
                {
                    cfg.ConnectEndpointConfigurationObserver(
                        new Outbox.ConsumerOutboxEndpointConfigurationObserver(
                            context, options.ConsumerOutboxEndpointConfigurator));
                }

                // Custom RabbitMQ bus configuration hook
                options.ConfigureRabbitMqBusAction?.Invoke(context, cfg);

                cfg.ConfigureEndpoints(context);
            });
        });

        // Health checks
        if (options.Options.EnableHealthChecks)
        {
            var rabbitMqOptions = options.Options.RabbitMq;
            var uriBuilder = new UriBuilder("amqp", rabbitMqOptions.Host, rabbitMqOptions.Port, rabbitMqOptions.VirtualHost)
            {
                UserName = Uri.EscapeDataString(rabbitMqOptions.Username),
                Password = Uri.EscapeDataString(rabbitMqOptions.Password)
            };
            services.AddHealthChecks()
                .AddRabbitMQ(uriBuilder.Uri, name: "rabbitmq")
                .AddCheck<MassTransitBusHealthCheck>("mw-masstransit-bus");
        }

        return services;
    }

    /// <summary>
    /// Binds MassTransitOptions from a configuration section.
    /// </summary>
    public static MassTransitMessagingOptions BindOptions(
        this MassTransitMessagingOptions messagingOptions,
        IConfiguration configuration,
        string sectionName = MassTransitOptions.SectionName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(sectionName);
        section.Bind(messagingOptions.Options);
        return messagingOptions;
    }

    /// <summary>
    /// Adds transactional outbox support using Entity Framework Core.
    /// </summary>
    public static IBusRegistrationConfigurator AddEntityFrameworkOutbox<TDbContext>(
        this IBusRegistrationConfigurator configurator,
        Action<IEntityFrameworkOutboxConfigurator>? configureOutbox = null,
        OutboxDatabaseProvider provider = OutboxDatabaseProvider.SqlServer)
        where TDbContext : DbContext
    {
        // Call MassTransit's extension method explicitly to avoid infinite recursion
        EntityFrameworkOutboxConfigurationExtensions.AddEntityFrameworkOutbox<TDbContext>(configurator, o =>
        {
            ApplyOutboxDatabaseProvider(o, provider);
            o.UseBusOutbox();
            configureOutbox?.Invoke(o);
        });

        return configurator;
    }

    /// <summary>
    /// Selects the transactional outbox store provider (SQL Server or PostgreSQL) so that
    /// MassTransit's EF Core outbox has a configured lock/store provider. Without this the
    /// bus fails to start because MassTransit 8.2.5 cannot resolve the outbox lock provider.
    /// </summary>
    internal static void ApplyOutboxDatabaseProvider(
        IEntityFrameworkOutboxConfigurator configurator,
        OutboxDatabaseProvider provider)
    {
        switch (provider)
        {
            case OutboxDatabaseProvider.PostgreSql:
                configurator.UsePostgres();
                break;
            default:
                configurator.UseSqlServer();
                break;
        }
    }
}

/// <summary>
/// Options for configuring MassTransit messaging infrastructure.
/// </summary>
public class MassTransitMessagingOptions
{
    public MassTransitOptions Options { get; set; } = new();
    public List<Assembly> ConsumerAssemblies { get; } = new();
    public Action<IBusRegistrationConfigurator>? ConfigureConsumersAction { get; set; }
    public Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? ConfigureRabbitMqBusAction { get; set; }
    public Action<IBusRegistrationConfigurator>? OutboxConfigurator { get; set; }

    /// <summary>
    /// When set (by <see cref="UseEntityFrameworkInboxOutbox{TDbContext}"/>), applied to every
    /// receive endpoint via an endpoint configuration observer so the consumer-side outbox
    /// (inbox de-duplication + transactional publish buffering) is actually active. The bus
    /// outbox alone does NOT cover publishes made from inside a consumer scope.
    /// </summary>
    public Action<IBusRegistrationContext, IReceiveEndpointConfigurator>? ConsumerOutboxEndpointConfigurator { get; set; }

    public MassTransitMessagingOptions AddConsumersFromAssembly(Assembly assembly)
    {
        ConsumerAssemblies.Add(assembly);
        return this;
    }

    public MassTransitMessagingOptions ConfigureConsumers(Action<IBusRegistrationConfigurator> configure)
    {
        ConfigureConsumersAction = configure;
        return this;
    }

    public MassTransitMessagingOptions ConfigureRabbitMqBus(
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator> configure)
    {
        ConfigureRabbitMqBusAction = configure;
        return this;
    }

    /// <summary>
    /// Configures transactional outbox with Entity Framework Core.
    /// This is the recommended single entry point for outbox configuration.
    /// Enables publisher-side outbox by default.
    /// </summary>
    public MassTransitMessagingOptions UseEntityFrameworkOutbox<TDbContext>(
        Action<IEntityFrameworkOutboxConfigurator>? configureOutbox = null,
        OutboxDatabaseProvider provider = OutboxDatabaseProvider.SqlServer)
        where TDbContext : DbContext
    {
        OutboxConfigurator = cfg => EntityFrameworkOutboxConfigurationExtensions.AddEntityFrameworkOutbox<TDbContext>(cfg, o =>
        {
            MassTransitServiceCollectionExtensions.ApplyOutboxDatabaseProvider(o, provider);
            o.UseBusOutbox();
            configureOutbox?.Invoke(o);
        });
        return this;
    }

    /// <summary>
    /// Configures consumer-side outbox (InboxState) with Entity Framework Core.
    /// This enables idempotent message delivery on the consumer side.
    /// Both publisher-side and consumer-side outbox are enabled: in addition to the bus outbox,
    /// an endpoint configuration observer applies <c>UseEntityFrameworkOutbox</c> to EVERY receive
    /// endpoint, so publishes made from inside a consumer are stored transactionally with the
    /// consumer's DbContext changes and delivered only after the transaction commits.
    /// (Before 1.0.5 this method configured the bus outbox only — consumer publishes silently
    /// bypassed the outbox via the ConsumeContext and raced the database commit.)
    /// Requires the DbContext model to include AddInboxStateEntity/AddOutboxMessageEntity/
    /// AddOutboxStateEntity and the corresponding tables to exist.
    /// </summary>
    public MassTransitMessagingOptions UseEntityFrameworkInboxOutbox<TDbContext>(
        Action<IEntityFrameworkOutboxConfigurator>? configureOutbox = null,
        OutboxDatabaseProvider provider = OutboxDatabaseProvider.SqlServer)
        where TDbContext : DbContext
    {
        OutboxConfigurator = cfg => EntityFrameworkOutboxConfigurationExtensions.AddEntityFrameworkOutbox<TDbContext>(cfg, o =>
        {
            MassTransitServiceCollectionExtensions.ApplyOutboxDatabaseProvider(o, provider);
            o.UseBusOutbox();
            o.QueryDelay = TimeSpan.FromSeconds(1);
            o.DuplicateDetectionWindow = TimeSpan.FromMinutes(5);
            configureOutbox?.Invoke(o);
        });
        ConsumerOutboxEndpointConfigurator = (context, endpoint) =>
            endpoint.UseEntityFrameworkOutbox<TDbContext>(context);
        return this;
    }
}
