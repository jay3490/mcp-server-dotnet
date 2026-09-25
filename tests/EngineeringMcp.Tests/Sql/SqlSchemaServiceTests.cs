using EngineeringMcp.Sql;
using EngineeringMcp.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringMcp.Tests.Sql;

public sealed class SqlSchemaServiceTests : IDisposable
{
    private readonly DemoDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Lists_tables_and_views_with_their_columns()
    {
        var schema = await _db.CreateSchemaService().GetSchemaAsync(table: null, CancellationToken.None);

        Assert.Equal(["deployment_stats", "deployments", "incidents", "services", "teams"], schema.Select(t => t.Name));
        Assert.Equal("view", schema.Single(t => t.Name == "deployment_stats").Kind);

        var teams = schema.Single(t => t.Name == "teams");
        Assert.Equal("table", teams.Kind);
        Assert.Equal(["id", "name", "chat_channel"], teams.Columns.Select(c => c.Name));
        Assert.True(teams.Columns[0].IsPrimaryKey);
        Assert.False(teams.Columns[1].IsNullable);
        Assert.Equal("TEXT", teams.Columns[1].DataType);
    }

    [Fact]
    public async Task Filters_by_table_name_case_insensitively()
    {
        var schema = await _db.CreateSchemaService().GetSchemaAsync("INCIDENTS", CancellationToken.None);

        var incidents = Assert.Single(schema);
        Assert.Equal("incidents", incidents.Name);
        Assert.True(incidents.Columns.Single(c => c.Name == "resolved_at").IsNullable);
    }

    [Fact]
    public async Task Unknown_table_yields_an_empty_list()
    {
        var schema = await _db.CreateSchemaService().GetSchemaAsync("missing", CancellationToken.None);

        Assert.Empty(schema);
    }

    [Fact]
    public void Seeding_is_skipped_when_the_database_already_exists()
    {
        var seeder = new SqliteDemoSeeder(_db.WrappedOptions, NullLogger<SqliteDemoSeeder>.Instance);

        Assert.False(seeder.EnsureCreated());
    }
}
