using System.ComponentModel;
using System.Globalization;
using System.Text;
using EngineeringMcp.Formatting;
using EngineeringMcp.Server;
using EngineeringMcp.Sql;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EngineeringMcp.Tools;

[McpServerToolType]
public sealed class SqlTools(SqlQueryService queries, SqlSchemaService schema)
{
    [McpServerTool(Name = ToolNames.SqlQuery, Title = "Run a read-only SQL query", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Runs one read-only SELECT (or WITH ... SELECT) statement against the engineering database and returns the rows " +
        "as a markdown table. Writes, DDL, multiple statements and side-effecting functions are rejected. " +
        "Results are capped at a server-side row limit; call sql_schema first to see the tables.")]
    public Task<CallToolResult> QueryAsync(
        [Description("A single SELECT statement. A trailing semicolon is allowed.")] string sql,
        [Description("Maximum rows to return. Capped by the server's configured limit.")] int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        return ToolResults.RunAsync(async () =>
        {
            var result = await queries.ExecuteAsync(sql, maxRows, cancellationToken).ConfigureAwait(false);

            var text = new StringBuilder();
            if (result.Columns.Count > 0)
            {
                text.Append(MarkdownTable.Render(result.Columns, result.Rows));
            }

            text.Append(CultureInfo.InvariantCulture, $"\n{result.Rows.Count} row(s) in {result.ElapsedMilliseconds} ms");
            if (result.Truncated)
            {
                text.Append(CultureInfo.InvariantCulture, $"; stopped at the {result.RowLimit}-row limit, more rows exist");
            }

            text.Append('.');

            return ToolResults.Success(text.ToString(), new
            {
                columns = result.Columns,
                rows = result.Rows,
                rowCount = result.Rows.Count,
                truncated = result.Truncated,
                rowLimit = result.RowLimit,
                elapsedMs = result.ElapsedMilliseconds,
            });
        });
    }

    [McpServerTool(Name = ToolNames.SqlSchema, Title = "List database tables and columns", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Lists the tables and views in the engineering database with their columns, types, nullability and primary keys.")]
    public Task<CallToolResult> SchemaAsync(
        [Description("Optional table or view name to describe; omit to list everything.")] string? table = null,
        CancellationToken cancellationToken = default)
    {
        return ToolResults.RunAsync(async () =>
        {
            var tables = await schema.GetSchemaAsync(table, cancellationToken).ConfigureAwait(false);
            if (tables.Count == 0 && !string.IsNullOrWhiteSpace(table))
            {
                throw new ToolErrorException(ToolErrorCode.NotFound, $"No table or view named '{table}'.");
            }

            return ToolResults.Success(Describe(schema.Dialect, tables), new { dialect = schema.Dialect, tables });
        });
    }

    internal static string Describe(SqlDialect dialect, IReadOnlyList<TableSchema> tables)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Database dialect: {dialect}. {tables.Count} table(s) and view(s).\n");
        foreach (var t in tables)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n### {t.Name} ({t.Kind})\n");
            foreach (var c in t.Columns)
            {
                text.Append(CultureInfo.InvariantCulture, $"- {c.Name} {c.DataType}");
                if (c.IsPrimaryKey)
                {
                    text.Append(", primary key");
                }

                if (!c.IsNullable)
                {
                    text.Append(", not null");
                }

                text.Append('\n');
            }
        }

        return text.ToString();
    }
}
