using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MW.Hosting.AspNetCore.Abstractions;
using MW.Hosting.AspNetCore.Consul;
using MW.Hosting.AspNetCore.Cors;
using MW.Hosting.AspNetCore.HealthChecks;
using MW.Hosting.AspNetCore.Logging;
using MW.Hosting.AspNetCore.Options;

namespace MW.Hosting.AspNetCore.Extensions;

public static class HostingServiceCollectionExtensions
{
    public static IServiceCollection AddMyBidHostingDefaults(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // CORS
        if (configuration.GetSection("Cors").Exists())
            services.AddDefaultCors(configuration);

        // Health checks
        services.AddDefaultHealthChecks();

        // Health endpoint options
        if (configuration.GetSection("HealthEndpoints").Exists())
        {
            services.Configure<HealthEndpointOptions>(configuration.GetSection("HealthEndpoints"));
            services.AddSingleton<IValidateOptions<HealthEndpointOptions>, HealthEndpointOptionsValidator>();
        }

        // Consul
        if (configuration.GetSection("Consul").Exists())
        {
            services.AddConsulClient(configuration);
            services.AddConsulRegistration(configuration);
        }

        // Logging services
        services.AddHttpContextAccessor();
        services.AddSingleton<ICurrentUserAccessor, HttpCurrentUserAccessor>();
        services.AddSingleton<MW.Messaging.Context.IPublishUserContext, MW.Hosting.AspNetCore.Logging.HttpPublishUserContext>();
        services.AddSingleton<IRequestTraceAccessor, HttpRequestTraceAccessor>();

        return services;
    }
}
