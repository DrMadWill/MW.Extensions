namespace MW.Projections.Options;

/// <summary>
/// Options for <see cref="Extensions.ProjectionsServiceCollectionExtensions.AddProjectionRepairRequester"/>.
/// </summary>
public sealed class ProjectionRepairOptions
{
    /// <summary>
    /// A second identical <c>(ReferenceType, ReferenceId)</c> repair request published within this
    /// window of the first is suppressed instead of re-published. Guards against request storms
    /// (e.g. a sweep and a read-through both noticing the same drift). Default: 30 seconds.
    /// </summary>
    public TimeSpan DedupWindow { get; set; } = TimeSpan.FromSeconds(30);
}
