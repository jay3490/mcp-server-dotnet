using EngineeringMcp.Configuration;
using EngineeringMcp.Docs;
using EngineeringMcp.Git;
using EngineeringMcp.Health;
using EngineeringMcp.Server;
using EngineeringMcp.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EngineeringMcp;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the data-access services behind the tools: SQL, docs, git and health checks.
    /// </summary>
    /// <param name="baseDirectory">
    /// Directory that relative paths in configuration are resolved against. Defaults to the
    /// application's base directory.
    /// </param>
    public static IServiceCollection AddEngineeringData(
        this IServiceCollection services,
        IConfiguration configuration,
        string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var root = baseDirectory ?? AppContext.BaseDirectory;

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .PostConfigure(o =>
            {
                if (o.Provider == SqlDialect.Sqlite)
                {
                    o.ConnectionString = PathResolution.ResolveSqliteConnectionString(o.ConnectionString, root);
                }

                if (o.SeedScript is not null)
                {
                    o.SeedScript = PathResolution.Resolve(o.SeedScript, root);
                }
            });

        services.AddSingleton<IReadOnlyDatabase>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DatabaseOptions>>();
            return options.Value.Provider switch
            {
                SqlDialect.PostgreSql => new PostgresReadOnlyDatabase(options),
                _ => new SqliteReadOnlyDatabase(options),
            };
        });
        services.AddSingleton<SqlQueryService>();
        services.AddSingleton<SqlSchemaService>();
        services.AddSingleton<SqliteDemoSeeder>();

        services.AddOptions<DocsOptions>()
            .Bind(configuration.GetSection(DocsOptions.SectionName))
            .PostConfigure(o => o.Root = PathResolution.Resolve(o.Root, root));
        services.AddSingleton<DocsSandbox>();
        services.AddSingleton<DocumentStore>();
        services.AddSingleton<DocsSearchService>();

        services.AddOptions<GitOptions>()
            .Bind(configuration.GetSection(GitOptions.SectionName))
            .PostConfigure(o => o.RepositoryPath = PathResolution.Resolve(o.RepositoryPath, root));
        services.AddSingleton<GitService>();

        services.AddOptions<HealthCheckOptions>().Bind(configuration.GetSection(HealthCheckOptions.SectionName));
        services.AddSingleton<HealthCheckService>();
        services.AddHttpClient(HealthCheckService.HttpClientName, client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("engineering-mcp-health/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // A redirect could point anywhere; report the 3xx instead of following it.
                AllowAutoRedirect = false,
                UseCookies = false,
                ConnectTimeout = TimeSpan.FromSeconds(5),
            });

        services.AddOptions<ToolPolicyOptions>()
            .Bind(configuration.GetSection(ToolPolicyOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ToolPolicyOptions>, ToolPolicyOptionsValidator>();
        services.AddOptions<AuditOptions>().Bind(configuration.GetSection(AuditOptions.SectionName));
        services.AddSingleton<ToolPolicy>();
        services.AddSingleton<ToolAuditLog>();
        services.AddSingleton<ToolCallPipeline>();

        return services;
    }
}
