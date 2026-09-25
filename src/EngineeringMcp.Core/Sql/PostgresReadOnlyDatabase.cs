using System.Data;
using System.Globalization;
using EngineeringMcp.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EngineeringMcp.Sql;

/// <summary>
/// PostgreSQL sessions that run inside a <c>READ ONLY</c> transaction with a server-side
/// <c>statement_timeout</c>. The transaction is always rolled back.
/// </summary>
public sealed class PostgresReadOnlyDatabase(IOptions<DatabaseOptions> options) : IReadOnlyDatabase, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource = NpgsqlDataSource.Create(options.Value.ConnectionString);

    public SqlDialect Dialect => SqlDialect.PostgreSql;

    public async Task<ReadOnlySession> OpenSessionAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transaction = await connection
                .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
                .ConfigureAwait(false);

            var timeoutMs = ((long)timeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
            await using (var setup = connection.CreateCommand())
            {
                setup.Transaction = transaction;
                setup.CommandText =
                    "SET TRANSACTION READ ONLY; " +
                    $"SET LOCAL statement_timeout = {timeoutMs}; " +
                    $"SET LOCAL lock_timeout = {timeoutMs};";
                await setup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            return new ReadOnlySession(connection, transaction, timeout);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
