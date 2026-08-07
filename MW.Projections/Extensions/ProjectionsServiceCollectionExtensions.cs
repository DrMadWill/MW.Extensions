using MW.Projections.Options;
using MW.Projections.Reconcile;
using MW.Projections.Repair;
using MW.Projections.Sweep;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MW.Projections.Extensions;

public static class ProjectionsServiceCollectionExtensions
{
    /// <summary>
    /// Owner-side registration: declares that <typeparamref name="TSource"/> repairs projections
    /// for <paramref name="referenceType"/>. Registers <typeparamref name="TSource"/> itself as a
    /// scoped service (so it can hold a scoped <c>DbContext</c>) and records the mapping in the
    /// shared registry that <see cref="ProjectionRepairRequestedConsumer"/> reads from.
    /// <para>
    /// Registering a second source for a <paramref name="referenceType"/> already registered on
    /// this <see cref="IServiceCollection"/> throws immediately — a configuration-time error,
    /// not a surprise the first time a repair message for that type arrives.
    /// </para>
    /// <para>
    /// The caller's own MassTransit setup must still add
    /// <c>busConfig.AddConsumer&lt;ProjectionRepairRequestedConsumer&gt;()</c> (or scan this
    /// package's assembly) so the consumer actually gets an endpoint — this method only wires DI.
    /// </para>
    /// </summary>
    public static IServiceCollection AddProjectionRepairSource<TSource>(
        this IServiceCollection services,
        string referenceType)
        where TSource : class, IProjectionRepairSource
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceType);

        var builder = services.GetOrAddRepairSourceRegistryBuilder();
        builder.Register(referenceType, typeof(TSource));

        services.TryAddScoped<TSource>();
        services.TryAddScoped<ProjectionRepairRequestedConsumer>();
        services.TryAddSingleton<IProjectionRepairSourceRegistry>(
            sp => sp.GetRequiredService<ProjectionRepairSourceRegistryBuilder>().Build());

        return services;
    }

    /// <summary>
    /// Consumer-side registration: adds <see cref="IProjectionRepairRequester"/>, backed by
    /// MassTransit's <see cref="MassTransit.IPublishEndpoint"/> (see
    /// <see cref="ProjectionRepairRequester"/> for why) and the dedup cache described by
    /// <paramref name="configure"/> (defaults to <see cref="ProjectionRepairOptions.DedupWindow"/>
    /// of 30 seconds).
    /// </summary>
    public static IServiceCollection AddProjectionRepairRequester(
        this IServiceCollection services,
        Action<ProjectionRepairOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ProjectionRepairDedupCache>();

        services.AddOptions<ProjectionRepairOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddScoped<IProjectionRepairRequester, ProjectionRepairRequester>();

        return services;
    }

    /// <summary>
    /// Registers the sweep hosted service for <typeparamref name="TKey"/>: a periodic background
    /// pass over <typeparamref name="TSource"/> (an <see cref="IProjectionSweepSource{TKey}"/>)
    /// that reconciles every key via <typeparamref name="TReconciler"/> (an
    /// <see cref="IProjectionReconciler{TKey}"/>). Both are registered scoped and resolved from a
    /// fresh scope once per run — see <see cref="ProjectionSweepService{TKey}"/> for why.
    /// <para>
    /// Disabled by default (<see cref="ProjectionSweepOptions.Enabled"/> = <see langword="false"/>);
    /// the consuming service must opt in explicitly via <paramref name="configure"/> on exactly ONE
    /// host if the sweep must not run twice (e.g. an API host and a WebUI host sharing the same
    /// package reference — see Task 9).
    /// </para>
    /// </summary>
    public static IServiceCollection AddProjectionSweep<TKey, TSource, TReconciler>(
        this IServiceCollection services,
        Action<ProjectionSweepOptions>? configure = null)
        where TSource : class, IProjectionSweepSource<TKey>
        where TReconciler : class, IProjectionReconciler<TKey>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<ProjectionSweepOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddScoped<IProjectionSweepSource<TKey>, TSource>();
        services.TryAddScoped<IProjectionReconciler<TKey>, TReconciler>();
        services.AddHostedService<ProjectionSweepService<TKey>>();

        return services;
    }

    /// <summary>
    /// Finds the <see cref="ProjectionRepairSourceRegistryBuilder"/> singleton already registered
    /// on this <see cref="IServiceCollection"/>, or creates and registers one. Kept as a directly-
    /// held instance (not resolved through a built provider — none exists yet during
    /// configuration) so that duplicate registration can throw synchronously, matching the
    /// house rule that a bad wiring decision must fail at startup, not on the first message.
    /// </summary>
    private static ProjectionRepairSourceRegistryBuilder GetOrAddRepairSourceRegistryBuilder(
        this IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(ProjectionRepairSourceRegistryBuilder)
                && descriptor.ImplementationInstance is ProjectionRepairSourceRegistryBuilder existing)
            {
                return existing;
            }
        }

        var builder = new ProjectionRepairSourceRegistryBuilder();
        services.AddSingleton(builder);
        return builder;
    }
}
