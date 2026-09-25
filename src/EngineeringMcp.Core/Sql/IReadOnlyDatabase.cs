using System.Data.Common;

namespace EngineeringMcp.Sql;

/// <summary>Opens database sessions that cannot write.</summary>
public interface IReadOnlyDatabase
{
    SqlDialect Dialect { get; }

    /// <summary>
    /// Opens a connection whose session is read-only and whose statements are cancelled after
    /// <paramref name="timeout"/>.
    /// </summary>
    Task<ReadOnlySession> OpenSessionAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>A connection plus the transaction (if any) that makes it read-only.</summary>
public sealed class ReadOnlySession : IAsyncDisposable
{
    private readonly IAsyncDisposable? _cancellationHook;

    internal ReadOnlySession(DbConnection connection, DbTransaction? transaction, TimeSpan timeout, IAsyncDisposable? cancellationHook = null)
    {
        Connection = connection;
        Transaction = transaction;
        Timeout = timeout;
        _cancellationHook = cancellationHook;
    }

    public DbConnection Connection { get; }

    public DbTransaction? Transaction { get; }

    public TimeSpan Timeout { get; }

    public DbCommand CreateCommand(string sql)
    {
        var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = Transaction;
        command.CommandTimeout = (int)Math.Ceiling(Timeout.TotalSeconds);
        return command;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cancellationHook is not null)
        {
            await _cancellationHook.DisposeAsync().ConfigureAwait(false);
        }

        if (Transaction is not null)
        {
            // Nothing was written; rolling back simply ends the read-only transaction.
            await Transaction.RollbackAsync().ConfigureAwait(false);
            await Transaction.DisposeAsync().ConfigureAwait(false);
        }

        await Connection.DisposeAsync().ConfigureAwait(false);
    }
}
