namespace MW.Projections.Repair;

/// <summary>
/// Implemented by consuming services to ask an owner to repair one entity's projection.
/// Registered via <c>services.AddProjectionRepairRequester()</c>.
/// </summary>
public interface IProjectionRepairRequester
{
    /// <summary>
    /// Publishes a <see cref="ProjectionRepairRequested"/> for <paramref name="referenceType"/>/
    /// <paramref name="referenceId"/>, unless an identical request was already published inside
    /// the configured dedup window (see <see cref="Options.ProjectionRepairOptions.DedupWindow"/>).
    /// </summary>
    Task RequestAsync(string referenceType, string referenceId, CancellationToken cancellationToken);
}
