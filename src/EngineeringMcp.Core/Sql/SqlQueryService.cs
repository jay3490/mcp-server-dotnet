using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using EngineeringMcp.Configuration;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Sql;

/// <summary>Rows returned by <see cref="SqlQueryService"/>, already rendered as display strings.</summary>
public sealed record SqlQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    bool Truncated,
    int RowLimit,
    long ElapsedMilliseconds);

/// <summary>Validates and runs read-only queries with a row cap and a timeout.</summary>
public sealed class SqlQueryService(IReadOnlyDatabase database, IOptions<DatabaseOptions> options)
{
    private readonly DatabaseOptions _options = options.Value;

    public SqlDialect Dialect => database.Dialect;

    public async Task<SqlQueryResult> ExecuteAsync(string sql, int? maxRows, CancellationToken cancellationToken)
    {
        var validation = SqlValidator.Validate(sql, database.Dialect);
        if (!validation.IsValid)
        {
            throw new ToolErrorException(ToolErrorCode.InvalidArgument, $"Query rejected: {validation.Error}");
        }

        var rowLimit = Math.Clamp(maxRows ?? _options.MaxRows, 1, Math.Max(1, _options.MaxRows));
        var timeout = _options.CommandTimeout;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var token = timeoutSource.Token;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var session = await database.OpenSessionAsync(timeout, token).ConfigureAwait(false);
            await using var command = session.CreateCommand(validation.Statement);
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, token)
                .ConfigureAwait(false);

            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            var rows = new List<IReadOnlyList<string?>>();
            var truncated = false;

            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (rows.Count == rowLimit)
                {
                    truncated = true;
                    break;
                }

                rows.Add(ReadRow(reader));
            }

            return new SqlQueryResult(columns, rows, truncated, rowLimit, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ToolErrorException(
                ToolErrorCode.Timeout,
                $"Query did not finish within {timeout.TotalSeconds:0.#} seconds and was cancelled.",
                ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            // The caller (for example the per-tool time limit) gave up; report it as cancellation.
            throw new OperationCanceledException("The query was cancelled by the caller.", ex, cancellationToken);
        }
        catch (DbException ex)
        {
            throw new ToolErrorException(ToolErrorCode.QueryFailed, $"The database reported an error: {ex.Message}", ex);
        }
    }

    private static string?[] ReadRow(DbDataReader reader)
    {
        var values = new string?[reader.FieldCount];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = FormatValue(reader.IsDBNull(i) ? null : reader.GetValue(i));
        }

        return values;
    }

    internal static string? FormatValue(object? value) => value switch
    {
        null => null,
        byte[] bytes => $"<{bytes.Length} bytes>",
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
