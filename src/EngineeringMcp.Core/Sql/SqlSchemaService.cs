using System.Data;
using System.Data.Common;
using EngineeringMcp.Configuration;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Sql;

public sealed record ColumnSchema(string Name, string DataType, bool IsNullable, bool IsPrimaryKey);

public sealed record TableSchema(string Name, string Kind, IReadOnlyList<ColumnSchema> Columns);

/// <summary>Lists tables, views and their columns using each engine's catalog.</summary>
public sealed class SqlSchemaService(IReadOnlyDatabase database, IOptions<DatabaseOptions> options)
{
    private const string SqliteCatalogQuery = """
        SELECT m.name, m.type, p.name, p.type, p."notnull", p.pk
        FROM sqlite_master AS m
        JOIN pragma_table_info(m.name) AS p
        WHERE m.type IN ('table', 'view')
          AND m.name NOT LIKE 'sqlite_%'
          AND (@table IS NULL OR m.name = @table COLLATE NOCASE)
        ORDER BY m.name, p.cid
        """;

    private const string PostgresCatalogQuery = """
        SELECT c.table_schema || '.' || c.table_name,
               CASE t.table_type WHEN 'VIEW' THEN 'view' ELSE 'table' END,
               c.column_name,
               c.data_type,
               CASE c.is_nullable WHEN 'YES' THEN 0 ELSE 1 END,
               CASE WHEN EXISTS (
                   SELECT 1
                   FROM information_schema.table_constraints tc
                   JOIN information_schema.key_column_usage k
                     ON k.constraint_name = tc.constraint_name
                    AND k.table_schema = tc.table_schema
                    AND k.table_name = tc.table_name
                   WHERE tc.constraint_type = 'PRIMARY KEY'
                     AND tc.table_schema = c.table_schema
                     AND tc.table_name = c.table_name
                     AND k.column_name = c.column_name) THEN 1 ELSE 0 END
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema AND t.table_name = c.table_name
        WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema')
          AND (@table IS NULL OR c.table_name = @table OR c.table_schema || '.' || c.table_name = @table)
        ORDER BY 1, c.ordinal_position
        """;

    public SqlDialect Dialect => database.Dialect;

    public async Task<IReadOnlyList<TableSchema>> GetSchemaAsync(string? table, CancellationToken cancellationToken)
    {
        var timeout = options.Value.CommandTimeout;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await using var session = await database.OpenSessionAsync(timeout, timeoutSource.Token).ConfigureAwait(false);
            await using var command = session.CreateCommand(
                database.Dialect == SqlDialect.PostgreSql ? PostgresCatalogQuery : SqliteCatalogQuery);

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.DbType = DbType.String;
            parameter.Value = string.IsNullOrWhiteSpace(table) ? DBNull.Value : table.Trim();
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync(timeoutSource.Token).ConfigureAwait(false);

            var tables = new List<TableSchema>();
            List<ColumnSchema>? columns = null;
            string? current = null;

            while (await reader.ReadAsync(timeoutSource.Token).ConfigureAwait(false))
            {
                var name = reader.GetString(0);
                if (!string.Equals(name, current, StringComparison.Ordinal))
                {
                    current = name;
                    columns = [];
                    tables.Add(new TableSchema(name, reader.GetString(1), columns));
                }

                columns!.Add(new ColumnSchema(
                    Name: reader.GetString(2),
                    DataType: reader.IsDBNull(3) || reader.GetString(3).Length == 0 ? "ANY" : reader.GetString(3),
                    IsNullable: Convert.ToInt64(reader.GetValue(4), System.Globalization.CultureInfo.InvariantCulture) == 0,
                    IsPrimaryKey: Convert.ToInt64(reader.GetValue(5), System.Globalization.CultureInfo.InvariantCulture) > 0));
            }

            return tables;
        }
        catch (Exception ex) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ToolErrorException(ToolErrorCode.Timeout, "Reading the schema timed out.", ex);
        }
        catch (DbException ex)
        {
            throw new ToolErrorException(ToolErrorCode.QueryFailed, $"Could not read the schema: {ex.Message}", ex);
        }
    }
}
