using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace MW.Projections.Repair;

/// <summary>
/// Read side of the repair-source map: <c>referenceType -&gt; IProjectionRepairSource</c>
/// implementation type. Built once, after DI configuration is complete, from
/// <see cref="ProjectionRepairSourceRegistryBuilder"/> — see that type for why duplicate
/// registration is rejected at configuration time rather than here.
/// </summary>
public interface IProjectionRepairSourceRegistry
{
    /// <summary>
    /// Looks up the <see cref="IProjectionRepairSource"/> implementation type registered for
    /// <paramref name="referenceType"/>. Returns <see langword="false"/> for an unknown type —
    /// it is the caller's job (the consumer) to decide that this is fatal.
    /// </summary>
    bool TryGetSourceType(string referenceType, [NotNullWhen(true)] out Type? sourceType);
}

/// <summary>
/// Immutable, thread-safe-for-reads implementation backed by a <see cref="FrozenDictionary{TKey,TValue}"/>.
/// </summary>
internal sealed class ProjectionRepairSourceRegistry : IProjectionRepairSourceRegistry
{
    private readonly FrozenDictionary<string, Type> _sources;

    public ProjectionRepairSourceRegistry(IReadOnlyDictionary<string, Type> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public bool TryGetSourceType(string referenceType, [NotNullWhen(true)] out Type? sourceType)
        => _sources.TryGetValue(referenceType, out sourceType);
}

/// <summary>
/// Write side, used only while services are being configured (i.e. before
/// <c>BuildServiceProvider</c>). Prior art (<c>Dr.EventBus.MassTransit.Repairs.RepairEvents</c>)
/// used a bare <c>static Dictionary</c> with <c>Dictionary.Add</c>, which is not thread-safe and
/// throws on the *first message* for a duplicate registration, long after the mistake was made.
/// Here, duplicate registration is a configuration-time error: it throws synchronously from
/// <c>AddProjectionRepairSource</c> itself, and the instance is per-<see cref="IServiceCollection"/>
/// (via <c>GetOrAddRepairSourceRegistryBuilder</c>) rather than static/shared across the process.
/// </summary>
internal sealed class ProjectionRepairSourceRegistryBuilder
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Type> _sources = new(StringComparer.Ordinal);

    public void Register(string referenceType, Type sourceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceType);
        ArgumentNullException.ThrowIfNull(sourceType);

        lock (_gate)
        {
            if (_sources.TryGetValue(referenceType, out var existing))
            {
                throw new InvalidOperationException(
                    $"A projection repair source is already registered for reference type '{referenceType}' " +
                    $"(existing: {existing.FullName}, attempted: {sourceType.FullName}). " +
                    "Each reference type may have exactly one repair source.");
            }

            _sources.Add(referenceType, sourceType);
        }
    }

    public IProjectionRepairSourceRegistry Build()
    {
        lock (_gate)
        {
            return new ProjectionRepairSourceRegistry(new Dictionary<string, Type>(_sources, StringComparer.Ordinal));
        }
    }
}
