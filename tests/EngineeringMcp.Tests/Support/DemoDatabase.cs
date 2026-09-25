using EngineeringMcp.Configuration;
using EngineeringMcp.Sql;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Tests.Support;

/// <summary>A fresh copy of the sample SQLite database in a temp directory.</summary>
public sealed class DemoDatabase : IDisposable
{
    private readonly TempDirectory _directory = new();

    public DemoDatabase(int maxRows = 200, int timeoutSeconds = 10)
    {
        Path = _directory.Combine("demo.db");
        Options = new DatabaseOptions
        {
            Provider = SqlDialect.Sqlite,
            // Pooling off so the file can be deleted as soon as the test finishes.
            ConnectionString = $"Data Source={Path};Pooling=False",
            SeedScript = SeedScriptPath,
            MaxRows = maxRows,
            CommandTimeoutSeconds = timeoutSeconds,
        };

        new SqliteDemoSeeder(Microsoft.Extensions.Options.Options.Create(Options), NullLogger<SqliteDemoSeeder>.Instance)
            .EnsureCreated();
    }

    public static string SeedScriptPath { get; } =
        System.IO.Path.Combine(AppContext.BaseDirectory, "samples", "data", "seed.sql");

    public string Path { get; }

    public DatabaseOptions Options { get; }

    public IOptions<DatabaseOptions> WrappedOptions => Microsoft.Extensions.Options.Options.Create(Options);

    public SqlQueryService CreateQueryService() =>
        new(new SqliteReadOnlyDatabase(WrappedOptions), WrappedOptions);

    public SqlSchemaService CreateSchemaService() =>
        new(new SqliteReadOnlyDatabase(WrappedOptions), WrappedOptions);

    public void Dispose() => _directory.Dispose();
}
