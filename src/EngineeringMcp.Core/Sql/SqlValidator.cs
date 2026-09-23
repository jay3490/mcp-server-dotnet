namespace EngineeringMcp.Sql;

/// <summary>Outcome of validating a statement submitted to <c>sql_query</c>.</summary>
/// <param name="IsValid">Whether the statement may be executed.</param>
/// <param name="Statement">The statement with any trailing semicolon removed; empty when invalid.</param>
/// <param name="Error">Why the statement was rejected; <see langword="null"/> when valid.</param>
public sealed record SqlValidationResult(bool IsValid, string Statement, string? Error)
{
    internal static SqlValidationResult Accept(string statement) => new(true, statement, null);

    internal static SqlValidationResult Reject(string error) => new(false, string.Empty, error);
}

/// <summary>
/// Decides whether a piece of SQL is a single read-only query.
/// </summary>
/// <remarks>
/// This is the first of two layers. The executor also opens SQLite connections read-only and
/// runs PostgreSQL queries inside a <c>READ ONLY</c> transaction, so a statement that slips past
/// the validator still cannot write. The validator exists to give the caller a clear, early
/// answer and to block things a read-only transaction does not, such as sleeping or reading
/// server files.
/// </remarks>
public static class SqlValidator
{
    public const int MaxStatementLength = 20_000;

    /// <summary>
    /// Keywords that either start a non-query statement or make a query write or lock. They are
    /// matched as bare words only; quoting a column called "update" keeps it usable.
    /// </summary>
    private static readonly HashSet<string> ForbiddenKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "UPSERT", "INTO",
        "DROP", "CREATE", "ALTER", "TRUNCATE", "RENAME",
        "GRANT", "REVOKE", "ATTACH", "DETACH", "PRAGMA", "VACUUM", "REINDEX",
        "EXEC", "EXECUTE",
    };

    /// <summary>
    /// Functions with side effects outside the query: sleeping, file and network access, server
    /// administration, sequence mutation and advisory locks.
    /// </summary>
    private static readonly HashSet<string> ForbiddenFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        // PostgreSQL
        "pg_sleep", "pg_sleep_for", "pg_sleep_until",
        "pg_read_file", "pg_read_binary_file", "pg_ls_dir", "pg_stat_file",
        "lo_import", "lo_export", "lo_unlink", "lo_from_bytea", "lo_put",
        "dblink", "dblink_exec", "dblink_connect",
        "pg_terminate_backend", "pg_cancel_backend", "pg_reload_conf", "pg_rotate_logfile",
        "set_config", "nextval", "setval", "pg_notify",
        "query_to_xml", "query_to_xml_and_xmlschema", "cursor_to_xml",
        "database_to_xml", "schema_to_xml",
        // SQLite
        "load_extension", "readfile", "writefile", "edit", "fts3_tokenizer",
    };

    private static readonly string[] ForbiddenFunctionPrefixes = ["pg_advisory_", "pg_try_advisory_"];

    public static SqlValidationResult Validate(string? sql, SqlDialect dialect)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return SqlValidationResult.Reject("The query is empty.");
        }

        if (sql.Length > MaxStatementLength)
        {
            return SqlValidationResult.Reject($"The query is longer than {MaxStatementLength} characters.");
        }

        IReadOnlyList<SqlToken> tokens;
        try
        {
            tokens = SqlTokenizer.Tokenize(sql, dialect);
        }
        catch (SqlTokenizeException ex)
        {
            return SqlValidationResult.Reject($"{ex.Message} (at offset {ex.Position})");
        }

        if (tokens.Count == 0)
        {
            return SqlValidationResult.Reject("The query contains only comments.");
        }

        var firstSemicolon = IndexOf(tokens, SqlTokenKind.Semicolon);
        if (firstSemicolon >= 0 && firstSemicolon != tokens.Count - 1)
        {
            return SqlValidationResult.Reject("Only a single statement is allowed.");
        }

        var body = firstSemicolon >= 0 ? tokens.Take(firstSemicolon).ToList() : tokens.ToList();
        if (body.Count == 0)
        {
            return SqlValidationResult.Reject("The query is empty.");
        }

        var leading = body.FirstOrDefault(t => !t.IsSymbol('('));
        if (!leading.IsWord("SELECT") && !leading.IsWord("WITH"))
        {
            return SqlValidationResult.Reject("Only SELECT statements (optionally starting with WITH) are allowed.");
        }

        for (var i = 0; i < body.Count; i++)
        {
            var token = body[i];
            if (token.Kind != SqlTokenKind.Word)
            {
                continue;
            }

            if (ForbiddenKeywords.Contains(token.Text))
            {
                return SqlValidationResult.Reject($"Keyword '{token.Text.ToUpperInvariant()}' is not allowed in a read-only query.");
            }

            var next = i + 1 < body.Count ? body[i + 1] : default;

            if (token.IsWord("FOR") && (next.IsWord("SHARE") || next.IsWord("NO") || next.IsWord("KEY")))
            {
                return SqlValidationResult.Reject("Row-locking clauses (FOR SHARE, FOR KEY SHARE, FOR NO KEY UPDATE) are not allowed.");
            }

            if (next.IsSymbol('(') && IsForbiddenFunction(token.Text))
            {
                return SqlValidationResult.Reject($"Function '{token.Text.ToLowerInvariant()}' is not allowed.");
            }
        }

        var end = firstSemicolon >= 0 ? tokens[firstSemicolon].Position : sql.Length;
        return SqlValidationResult.Accept(sql[..end].Trim());
    }

    private static bool IsForbiddenFunction(string name) =>
        ForbiddenFunctions.Contains(name)
        || ForbiddenFunctionPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static int IndexOf(IReadOnlyList<SqlToken> tokens, SqlTokenKind kind)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == kind)
            {
                return i;
            }
        }

        return -1;
    }
}
