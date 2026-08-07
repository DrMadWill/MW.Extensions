using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MW.Projections.Extensions;
using MW.Projections.Options;
using MW.Projections.Repair;

namespace MW.Projections.Tests.Extensions;

public class ProjectionsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddProjectionRepairSource_Should_ThrowAtRegistrationTime_ForDuplicateReferenceType()
    {
        var services = new ServiceCollection();
        services.AddProjectionRepairSource<SourceA>("product");

        var act = () => services.AddProjectionRepairSource<SourceB>("product");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*product*")
            .WithMessage($"*{typeof(SourceA).FullName}*")
            .WithMessage($"*{typeof(SourceB).FullName}*");
    }

    [Fact]
    public void AddProjectionRepairSource_Should_NotThrow_ForDifferentReferenceTypes()
    {
        var services = new ServiceCollection();
        services.AddProjectionRepairSource<SourceA>("product");

        var act = () => services.AddProjectionRepairSource<SourceB>("variant");

        act.Should().NotThrow();
    }

    [Fact]
    public void AddProjectionRepairSource_Should_RegisterSourceAsResolvableFromScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProjectionRepairSource<SourceA>("product");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<SourceA>().Should().NotBeNull();
    }

    [Fact]
    public void AddProjectionRepairSource_Should_RegisterTheRegistry_ReflectingAllSources()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProjectionRepairSource<SourceA>("product");
        services.AddProjectionRepairSource<SourceB>("variant");

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IProjectionRepairSourceRegistry>();

        registry.TryGetSourceType("product", out var productType).Should().BeTrue();
        productType.Should().Be(typeof(SourceA));

        registry.TryGetSourceType("variant", out var variantType).Should().BeTrue();
        variantType.Should().Be(typeof(SourceB));
    }

    [Fact]
    public void AddProjectionRepairRequester_Should_RegisterRequesterDescriptor()
    {
        var services = new ServiceCollection();

        services.AddProjectionRepairRequester();

        services.Should().Contain(d => d.ServiceType == typeof(IProjectionRepairRequester));
    }

    [Fact]
    public void AddProjectionRepairRequester_Should_ApplyConfigureDelegateToOptions()
    {
        var services = new ServiceCollection();

        services.AddProjectionRepairRequester(o => o.DedupWindow = TimeSpan.FromSeconds(90));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<ProjectionRepairOptions>>().Value.DedupWindow
            .Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void AddProjectionRepairRequester_Should_DefaultDedupWindow_WhenNotConfigured()
    {
        var services = new ServiceCollection();

        services.AddProjectionRepairRequester();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<ProjectionRepairOptions>>().Value.DedupWindow
            .Should().Be(TimeSpan.FromSeconds(30));
    }

    private sealed class SourceA : IProjectionRepairSource
    {
        public Task RepairAsync(string referenceId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SourceB : IProjectionRepairSource
    {
        public Task RepairAsync(string referenceId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
