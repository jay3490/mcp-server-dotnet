using EngineeringMcp.Configuration;
using EngineeringMcp.Docs;
using EngineeringMcp.Git;
using EngineeringMcp.Health;
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
    public static IServiceCollection AddEngineeringData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));

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

        services.AddOptions<DocsOptions>().Bind(configuration.GetSection(DocsOptions.SectionName));
        services.AddSingleton<DocsSandbox>();
        services.AddSingleton<DocumentStore>();
        services.AddSingleton<DocsSearchService>();

        services.AddOptions<GitOptions>().Bind(configuration.GetSection(GitOptions.SectionName));
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

        return services;
    }
}
