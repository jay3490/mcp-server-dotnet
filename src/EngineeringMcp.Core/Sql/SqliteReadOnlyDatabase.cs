using EngineeringMcp.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SQLitePCL;

namespace EngineeringMcp.Sql;

/// <summary>
/// SQLite sessions opened with <c>Mode=ReadOnly</c> and <c>PRAGMA query_only</c>, whatever the
/// configured connection string asks for.
/// </summary>
public sealed class SqliteReadOnlyDatabase(IOptions<DatabaseOptions> options) : IReadOnlyDatabase
{
    private readonly string _connectionString = BuildConnectionString(options.Value.ConnectionString);

    public SqlDialect Dialect => SqlDialect.Sqlite;

    public async Task<ReadOnlySession> OpenSessionAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA query_only = ON;";
                await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // Microsoft.Data.Sqlite only checks the token between rows, so a single long step
            // (a large aggregate, a runaway recursive CTE) would ignore it. Interrupting the
            // connection stops the statement where it is.
            var interrupt = cancellationToken.Register(
                static state => raw.sqlite3_interrupt(((SqliteConnection)state!).Handle),
                connection);

            return new ReadOnlySession(connection, transaction: null, timeout, interrupt);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static string BuildConnectionString(string configured)
    {
        var builder = new SqliteConnectionStringBuilder(configured)
        {
            Mode = SqliteOpenMode.ReadOnly,
        };

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new InvalidOperationException("The SQLite connection string must name a Data Source.");
        }

        return builder.ToString();
    }
}
