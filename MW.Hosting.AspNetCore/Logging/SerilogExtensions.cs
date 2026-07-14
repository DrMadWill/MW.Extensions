using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MW.Hosting.AspNetCore.Options;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Graylog;
using Serilog.Sinks.Graylog.Core.Transport;

namespace MW.Hosting.AspNetCore.Logging;

public static class SerilogExtensions
{
    public static WebApplicationBuilder ConfigureDefaultSerilog(
        this WebApplicationBuilder builder,
        string graylogSectionName = "Graylog")
    {
        var configuration = builder.Configuration;

        var graylogOptions = new GraylogOptions();
        configuration.GetSection(graylogSectionName).Bind(graylogOptions);

        builder.Host.UseSerilog((context, services, loggerConfig) =>
        {
            const string template =
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] " +
                "[User:{UserName}] [Trace:{STraceId}] [Activity:{SActivityId}] " +
                "{Message:lj}{NewLine}{Exception}";
            loggerConfig
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("MachineName", Environment.MachineName)
                .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
                .Enrich.With(new UserEnricher(services.GetRequiredService<IHttpContextAccessor>()))
                .WriteTo.Console(outputTemplate: template);

            if (graylogOptions.Enabled)
            {
                var transportType = Enum.TryParse<TransportType>(graylogOptions.TransportType, true, out var parsed)
                    ? parsed
                    : TransportType.Udp;

                loggerConfig.WriteTo.Graylog(new GraylogSinkOptions
                {
                    HostnameOrAddress = graylogOptions.Host,
                    Port = graylogOptions.Port,
                    Facility = graylogOptions.Facility,
                    TransportType = transportType
                });
            }

            // Suppress health-probe request logs (Consul/k8s/load-balancer traffic) so they
            // don't flood the logs. Uses the same HealthEndpointOptions as the endpoint mapping,
            // so Path + Readiness + Liveness are all covered and stay in sync with the endpoints.
            var healthOptions = new HealthEndpointOptions();
            configuration.GetSection("HealthEndpoints").Bind(healthOptions);

            if (healthOptions.SuppressRequestLogging)
            {
                var healthPaths = healthOptions.AllPaths();

                // "RequestPath" is emitted by Serilog.AspNetCore's UseSerilogRequestLogging and by the
                // ASP.NET Core hosting log scope; "Path" is emitted by the framework's own
                // "Request starting/finished" diagnostics. Check both so probe traffic is suppressed
                // regardless of which pipeline produced the event.
                loggerConfig.Filter.ByExcluding(logEvent =>
                    healthPaths.Count > 0
                    && (IsHealthPathProperty(logEvent, "RequestPath", healthPaths)
                        || IsHealthPathProperty(logEvent, "Path", healthPaths)));
            }
        });

        return builder;
    }

    internal static bool IsHealthPathProperty(
        LogEvent logEvent,
        string propertyName,
        IReadOnlyList<string> healthPaths)
        => logEvent.Properties.TryGetValue(propertyName, out var value)
           && value is ScalarValue { Value: string path }
           && HealthEndpointOptions.MatchesHealthPath(path, healthPaths);
}
