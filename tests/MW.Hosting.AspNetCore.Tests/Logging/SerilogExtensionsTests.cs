using FluentAssertions;
using MW.Hosting.AspNetCore.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace MW.Hosting.AspNetCore.Tests.Logging;

public class SerilogExtensionsTests
{
    private static readonly IReadOnlyList<string> HealthPaths = new[] { "/api/health" };

    [Fact]
    public void IsHealthPathProperty_WhenRequestPathPropertyMatchesHealthPath_ReturnsTrue()
    {
        // "RequestPath" is emitted by Serilog.AspNetCore's UseSerilogRequestLogging.
        var logEvent = CreateLogEvent(("RequestPath", "/api/health"));

        var result = SerilogExtensions.IsHealthPathProperty(logEvent, "RequestPath", HealthPaths);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsHealthPathProperty_WhenPathPropertyMatchesHealthPath_ReturnsTrue()
    {
        // "Path" is emitted by ASP.NET Core's own "Request starting/finished" diagnostics.
        var logEvent = CreateLogEvent(("Path", "/api/health"));

        var result = SerilogExtensions.IsHealthPathProperty(logEvent, "Path", HealthPaths);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsHealthPathProperty_WhenRequestPathDoesNotMatchHealthPath_ReturnsFalse()
    {
        var logEvent = CreateLogEvent(("RequestPath", "/api/products"));

        var result = SerilogExtensions.IsHealthPathProperty(logEvent, "RequestPath", HealthPaths);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsHealthPathProperty_WhenNoPathPropertyIsPresent_ReturnsFalse()
    {
        var logEvent = CreateLogEvent(("UserName", "Anonymous"));

        var result = SerilogExtensions.IsHealthPathProperty(logEvent, "RequestPath", HealthPaths);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsHealthPathProperty_WhenRequestedPropertyNameIsAbsentButOtherPathPropertyIsPresent_ReturnsFalse()
    {
        // Event carries "Path" but caller asks about "RequestPath" -> must not cross-match;
        // callers are expected to check both property names explicitly (see ConfigureDefaultSerilog).
        var logEvent = CreateLogEvent(("Path", "/api/health"));

        var result = SerilogExtensions.IsHealthPathProperty(logEvent, "RequestPath", HealthPaths);

        result.Should().BeFalse();
    }

    private static LogEvent CreateLogEvent(params (string Name, string Value)[] properties)
    {
        var messageTemplate = new MessageTemplateParser().Parse("Test log message");
        var logEventProperties = properties.Select(p => new LogEventProperty(p.Name, new ScalarValue(p.Value)));

        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            messageTemplate,
            logEventProperties);
    }
}
