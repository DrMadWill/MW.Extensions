using FluentAssertions;
using MW.Projections.Options;

namespace MW.Projections.Tests.Options;

public class ProjectionRepairOptionsTests
{
    [Fact]
    public void DedupWindow_Should_DefaultTo30Seconds()
    {
        new ProjectionRepairOptions().DedupWindow.Should().Be(TimeSpan.FromSeconds(30));
    }
}
