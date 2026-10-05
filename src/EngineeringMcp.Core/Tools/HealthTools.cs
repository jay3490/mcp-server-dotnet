using System.ComponentModel;
using System.Globalization;
using EngineeringMcp.Formatting;
using EngineeringMcp.Health;
using EngineeringMcp.Server;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EngineeringMcp.Tools;

[McpServerToolType]
public sealed class HealthTools(HealthCheckService health)
{
    [McpServerTool(Name = ToolNames.HttpStatus, Title = "Check service health endpoints", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description(
        "Requests the health endpoints configured on the server and reports status code, latency and whether each " +
        "is healthy. Only endpoints from the server's allow-list can be checked; pick one by name or omit it to check all.")]
    public async Task<CallToolResult> CheckAsync(
        [Description("Endpoint name from the allow-list (or its exact URL). Omit to check every endpoint.")] string? endpoint = null,
        CancellationToken cancellationToken = default)
    {
        var known = string.Join(", ", health.Endpoints.Select(e => e.Name));

        return await ToolResults.RunAsync(async () =>
        {
            var statuses = await health.CheckAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var text = MarkdownTable.Render(
                ["endpoint", "healthy", "status", "latency (ms)", "error"],
                statuses.Select(s => (IReadOnlyList<string?>)
                [
                    s.Name,
                    s.Healthy ? "yes" : "no",
                    s.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "-",
                    s.LatencyMilliseconds.ToString(CultureInfo.InvariantCulture),
                    s.Error ?? string.Empty,
                ]));

            var healthy = statuses.Count(s => s.Healthy);
            return ToolResults.Success($"{text}\n{healthy} of {statuses.Count} healthy. Allow-listed endpoints: {known}.", new { endpoints = statuses });
        }).ConfigureAwait(false);
    }
}
