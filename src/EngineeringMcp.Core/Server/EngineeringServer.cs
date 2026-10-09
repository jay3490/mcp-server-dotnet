using System.Reflection;
using EngineeringMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EngineeringMcp.Server;

/// <summary>Wires the engineering-data primitives into an MCP server, independent of transport.</summary>
public static class EngineeringServer
{
    public const string Name = "engineering-mcp";

    public static string Version { get; } =
        typeof(EngineeringServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public const string Instructions =
        "Read-only access to engineering data. Use sql_schema before sql_query; queries must be a single SELECT. " +
        "Use search_docs to find runbooks and ADRs, then read_doc to read one. git_log and git_diff_summary describe " +
        "recent changes to the configured repository. http_status checks allow-listed health endpoints. " +
        "Failed calls return isError with a structured { error: { code, message } } payload.";

    public static void ConfigureOptions(McpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ServerInfo = new Implementation { Name = Name, Title = "Engineering data", Version = Version };
        options.ServerInstructions = Instructions;
    }

    /// <summary>
    /// Registers every tool plus the filters that hide disabled tools from <c>tools/list</c> and
    /// run each <c>tools/call</c> through <see cref="ToolCallPipeline"/>. Call after choosing a
    /// transport; the services come from <c>AddEngineeringData</c>.
    /// </summary>
    public static IMcpServerBuilder WithEngineeringTools(this IMcpServerBuilder builder)
    {
        return builder
            .WithTools<SqlTools>()
            .WithTools<DocsTools>()
            .WithTools<GitTools>()
            .WithTools<HealthTools>()
            .WithRequestFilters(filters =>
            {
                filters.AddListToolsFilter(next => async (context, cancellationToken) =>
                {
                    var result = await next(context, cancellationToken).ConfigureAwait(false);
                    var policy = Resolve<ToolPolicy>(context.Services ?? context.Server.Services);
                    result.Tools = result.Tools.Where(t => policy.IsAllowed(t.Name)).ToList();
                    return result;
                });

                filters.AddCallToolFilter(next => (context, cancellationToken) =>
                    Resolve<ToolCallPipeline>(context.Services ?? context.Server.Services)
                        .InvokeAsync(next, context, cancellationToken));
            });
    }

    private static T Resolve<T>(IServiceProvider? services)
        where T : notnull =>
        (services ?? throw new InvalidOperationException("The MCP request has no service provider."))
            .GetRequiredService<T>();
}
