using FluentAssertions;
using MW.Hosting.AspNetCore.Options;

namespace MW.Hosting.AspNetCore.Tests.Options;

public class HealthEndpointOptionsTests
{
    [Fact]
    public void AllPaths_WhenOnlyPathIsSet_ReturnsSingleElement()
    {
        var options = new HealthEndpointOptions
        {
            Path = "/api/health",
            ReadinessPath = null,
            LivenessPath = null
        };

        var result = options.AllPaths();

        result.Should().ContainSingle().Which.Should().Be("/api/health");
    }

    [Fact]
    public void AllPaths_WhenPathReadinessAndLivenessAreSet_ReturnsAllThree()
    {
        var options = new HealthEndpointOptions
        {
            Path = "/api/health",
            ReadinessPath = "/api/ready",
            LivenessPath = "/api/live"
        };

        var result = options.AllPaths();

        result.Should().BeEquivalentTo(new[] { "/api/health", "/api/ready", "/api/live" });
    }

    [Fact]
    public void AllPaths_WhenPathsHaveTrailingSlashOrQueryString_NormalizesThem()
    {
        var options = new HealthEndpointOptions
        {
            Path = "/api/health/",
            ReadinessPath = "/api/ready?x=1",
            LivenessPath = "/api/live/?y=2"
        };

        var result = options.AllPaths();

        result.Should().BeEquivalentTo(new[] { "/api/health", "/api/ready", "/api/live" });
    }

    [Fact]
    public void AllPaths_WhenPathsAreDuplicateCaseInsensitively_Deduplicates()
    {
        var options = new HealthEndpointOptions
        {
            Path = "/api/health",
            ReadinessPath = "/API/HEALTH",
            LivenessPath = "/api/health/"
        };

        var result = options.AllPaths();

        result.Should().ContainSingle().Which.Should().Be("/api/health");
    }

    [Theory]
    [InlineData("/API/HEALTH")]
    [InlineData("/api/health/")]
    [InlineData("/api/health?x=1")]
    public void MatchesHealthPath_WhenRequestPathVariesByCaseSlashOrQuery_ReturnsTrue(string requestPath)
    {
        var healthPaths = new[] { "/api/health" };

        var result = HealthEndpointOptions.MatchesHealthPath(requestPath, healthPaths);

        result.Should().BeTrue();
    }

    [Fact]
    public void MatchesHealthPath_WhenRequestPathIsUnrelated_ReturnsFalse()
    {
        var healthPaths = new[] { "/api/health" };

        var result = HealthEndpointOptions.MatchesHealthPath("/api/products", healthPaths);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MatchesHealthPath_WhenRequestPathIsNullOrEmpty_ReturnsFalse(string? requestPath)
    {
        var healthPaths = new[] { "/api/health" };

        var result = HealthEndpointOptions.MatchesHealthPath(requestPath, healthPaths);

        result.Should().BeFalse();
    }

    [Fact]
    public void MatchesHealthPath_WhenHealthPathsIsEmpty_ReturnsFalse()
    {
        var result = HealthEndpointOptions.MatchesHealthPath("/api/health", Array.Empty<string>());

        result.Should().BeFalse();
    }
}
