using System.Diagnostics;
using EngineeringMcp.Sql;
using EngineeringMcp.Tests.Support;
using Microsoft.Data.Sqlite;

namespace EngineeringMcp.Tests.Sql;

public sealed class SqlQueryServiceTests : IDisposable
{
    private readonly DemoDatabase _db = new(maxRows: 50, timeoutSeconds: 1);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Returns_columns_and_rows()
    {
        var result = await _db.CreateQueryService()
            .ExecuteAsync("SELECT id, name, chat_channel FROM teams ORDER BY id", maxRows: null, CancellationToken.None);

        Assert.Equal(["id", "name", "chat_channel"], result.Columns);
        Assert.Equal(4, result.Rows.Count);
        Assert.Equal(["1", "Platform", "#team-platform"], result.Rows[0]);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Stops_at_the_requested_row_limit_and_flags_truncation()
    {
        var result = await _db.CreateQueryService()
            .ExecuteAsync("SELECT * FROM deployments", maxRows: 10, CancellationToken.None);

        Assert.Equal(10, result.Rows.Count);
        Assert.Equal(10, result.RowLimit);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task A_result_exactly_at_the_limit_is_not_truncated()
    {
        var result = await _db.CreateQueryService()
            .ExecuteAsync("SELECT * FROM teams", maxRows: 4, CancellationToken.None);

        Assert.Equal(4, result.Rows.Count);
        Assert.False(result.Truncated);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(null)]
    public async Task Requested_limit_never_exceeds_the_configured_cap(int? requested)
    {
        var result = await _db.CreateQueryService()
            .ExecuteAsync("SELECT * FROM deployments", requested, CancellationToken.None);

        Assert.Equal(50, result.RowLimit);
        Assert.Equal(50, result.Rows.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task Null_values_stay_null()
    {
        var result = await _db.CreateQueryService()
            .ExecuteAsync("SELECT resolved_at FROM incidents WHERE resolved_at IS NULL", null, CancellationToken.None);

        Assert.Single(result.Rows);
        Assert.Null(result.Rows[0][0]);
    }

    [Fact]
    public async Task Rejected_statements_raise_invalid_argument()
    {
        var error = await Assert.ThrowsAsync<ToolErrorException>(() => _db.CreateQueryService()
            .ExecuteAsync("DELETE FROM teams", null, CancellationToken.None));

        Assert.Equal(ToolErrorCode.InvalidArgument, error.Code);
        Assert.Contains("Only SELECT", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Database_errors_raise_query_failed()
    {
        var error = await Assert.ThrowsAsync<ToolErrorException>(() => _db.CreateQueryService()
            .ExecuteAsync("SELECT * FROM no_such_table", null, CancellationToken.None));

        Assert.Equal(ToolErrorCode.QueryFailed, error.Code);
        Assert.Contains("no_such_table", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runaway_queries_are_interrupted_at_the_timeout()
    {
        const string runaway = "WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c) SELECT count(*) FROM c";
        var stopwatch = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<ToolErrorException>(() => _db.CreateQueryService()
            .ExecuteAsync(runaway, null, CancellationToken.None));

        Assert.Equal(ToolErrorCode.Timeout, error.Code);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Sessions_cannot_write_even_when_the_validator_is_bypassed()
    {
        var database = new SqliteReadOnlyDatabase(_db.WrappedOptions);
        await using var session = await database.OpenSessionAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await using var command = session.CreateCommand("DELETE FROM teams");

        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        Assert.Contains("readonly", error.Message.Replace("-", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Connection_string_is_forced_to_read_only()
    {
        var connectionString = SqliteReadOnlyDatabase.BuildConnectionString("Data Source=x.db;Mode=ReadWriteCreate");

        Assert.Contains("Mode=ReadOnly", connectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void Formats_values_with_the_invariant_culture()
    {
        Assert.Null(SqlQueryService.FormatValue(null));
        Assert.Equal("<3 bytes>", SqlQueryService.FormatValue(new byte[] { 1, 2, 3 }));
        Assert.Equal("1.5", SqlQueryService.FormatValue(1.5d));
        Assert.Equal("true", SqlQueryService.FormatValue(true));
        Assert.Equal("2026-01-02T03:04:05.0000000", SqlQueryService.FormatValue(new DateTime(2026, 1, 2, 3, 4, 5)));
    }
}
