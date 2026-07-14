namespace MW.Hosting.AspNetCore.Options;

public class HealthEndpointOptions
{
    /// <summary>
    /// Canonical health-check path exposed by every MyBid service.
    /// Keep in sync with Consul "HealthCheckPath". Default: <c>/api/health</c>.
    /// </summary>
    public string Path { get; set; } = "/api/health";

    /// <summary>Optional readiness probe path (maps checks tagged "ready").</summary>
    public string? ReadinessPath { get; set; }

    /// <summary>Optional liveness probe path (maps checks tagged "live").</summary>
    public string? LivenessPath { get; set; }

    public bool UsePlainTextResponse { get; set; } = true;

    public string PlainText { get; set; } = "Ok";

    /// <summary>
    /// When true (default), request logs targeting the health endpoints are excluded
    /// from Serilog output so probe traffic (Consul, k8s, load balancers) does not flood the logs.
    /// </summary>
    public bool SuppressRequestLogging { get; set; } = true;

    /// <summary>
    /// All configured health paths (canonical + optional readiness/liveness), normalized
    /// (query stripped, trailing slash trimmed) and de-duplicated. Single source of truth
    /// used for both endpoint mapping and log filtering.
    /// </summary>
    public IReadOnlyList<string> AllPaths()
    {
        var paths = new List<string>();

        if (!string.IsNullOrWhiteSpace(Path))
            paths.Add(Path);
        if (!string.IsNullOrWhiteSpace(ReadinessPath))
            paths.Add(ReadinessPath!);
        if (!string.IsNullOrWhiteSpace(LivenessPath))
            paths.Add(LivenessPath!);

        return paths
            .Select(NormalizePath)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns true if <paramref name="requestPath"/> targets one of <paramref name="healthPaths"/>.
    /// Comparison ignores the query string, is case-insensitive and trailing-slash-insensitive.
    /// </summary>
    public static bool MatchesHealthPath(string? requestPath, IReadOnlyList<string> healthPaths)
    {
        if (string.IsNullOrEmpty(requestPath) || healthPaths.Count == 0)
            return false;

        var normalized = NormalizePath(requestPath);
        if (normalized.Length == 0)
            return false;

        for (var i = 0; i < healthPaths.Count; i++)
        {
            if (string.Equals(normalized, healthPaths[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormalizePath(string path)
    {
        var value = path;

        var queryIndex = value.IndexOf('?');
        if (queryIndex >= 0)
            value = value[..queryIndex];

        return value.TrimEnd('/');
    }
}
