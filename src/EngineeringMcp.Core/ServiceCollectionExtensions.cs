using EngineeringMcp.Configuration;
using EngineeringMcp.Docs;
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

        return services;
    }
}
