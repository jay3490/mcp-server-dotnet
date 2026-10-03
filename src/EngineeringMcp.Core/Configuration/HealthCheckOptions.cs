namespace EngineeringMcp.Configuration;

/// <summary>Settings for <c>http_status</c>: the only URLs the server will ever request.</summary>
public sealed class HealthCheckOptions
{
    public const string SectionName = "EngineeringMcp:HealthChecks";

    public IList<HealthEndpoint> Endpoints { get; set; } = [];

    /// <summary>Per-request timeout.</summary>
    public int TimeoutSeconds { get; set; } = 5;
}

public sealed class HealthEndpoint
{
    /// <summary>Short name callers use to pick the endpoint, e.g. <c>billing</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute http or https URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Status code that counts as healthy. When unset, any 2xx does.</summary>
    public int? ExpectedStatus { get; set; }
}
