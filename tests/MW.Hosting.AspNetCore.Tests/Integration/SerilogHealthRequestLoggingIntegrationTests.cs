using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MW.Hosting.AspNetCore.HealthChecks;
using MW.Hosting.AspNetCore.Logging;
using Serilog;

namespace MW.Hosting.AspNetCore.Tests.Integration;

/// <summary>
/// End-to-end coverage for the health-probe log suppression wired in
/// <see cref="SerilogExtensions.ConfigureDefaultSerilog"/>: builds a real host with
/// <c>UseSerilogRequestLogging</c> in the pipeline (as every MyBid service does) and asserts
/// on the actual console output, instead of only unit-testing the internal predicate.
/// </summary>
[Collection("Serilog Global Logger")]
public class SerilogHealthRequestLoggingIntegrationTests
{
    [Fact]
    public async Task RequestLogging_WhenSuppressionIsEnabled_ExcludesHealthProbeButLogsOtherRequests()
    {
        var originalOut = Console.Out;
        var writer = new StringWriter();

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddDefaultHealthChecks();
            builder.ConfigureDefaultSerilog();

            var app = builder.Build();

            Console.SetOut(writer);

            app.UseSerilogRequestLogging();
            app.MapDefaultHealthEndpoints();
            app.MapGet("/api/products", () => "Products");

            await app.StartAsync();
            var client = app.GetTestClient();

            await client.GetAsync("/api/health");
            await client.GetAsync("/api/products");

            Log.CloseAndFlush();

            var output = writer.ToString();

            output.Should().NotContain("/api/health");
            output.Should().Contain("/api/products");

            await app.StopAsync();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task RequestLogging_WhenSuppressionIsDisabled_LogsHealthProbeToo()
    {
        var originalOut = Console.Out;
        var writer = new StringWriter();

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddDefaultHealthChecks();
            // ConfigureDefaultSerilog binds HealthEndpointOptions directly from IConfiguration
            // (not from a DI-registered IOptions<HealthEndpointOptions>), so the suppression
            // toggle must be set on configuration, not via services.Configure(...).
            builder.Configuration["HealthEndpoints:SuppressRequestLogging"] = "false";
            builder.ConfigureDefaultSerilog();

            var app = builder.Build();

            Console.SetOut(writer);

            app.UseSerilogRequestLogging();
            app.MapDefaultHealthEndpoints();

            await app.StartAsync();
            var client = app.GetTestClient();

            await client.GetAsync("/api/health");

            Log.CloseAndFlush();

            var output = writer.ToString();

            output.Should().Contain("/api/health");

            await app.StopAsync();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
