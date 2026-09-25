using EngineeringMcp.Sql;

namespace EngineeringMcp.Configuration;

/// <summary>Settings for the <c>sql_query</c> and <c>sql_schema</c> tools.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "EngineeringMcp:Database";

    /// <summary>Which engine <see cref="ConnectionString"/> points at.</summary>
    public SqlDialect Provider { get; set; } = SqlDialect.Sqlite;

    /// <summary>
    /// ADO.NET connection string. For SQLite the server always forces <c>Mode=ReadOnly</c>,
    /// whatever this string says.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Hard cap on rows returned by one query. Callers may ask for fewer, never more.</summary>
    public int MaxRows { get; set; } = 200;

    /// <summary>How long a single query may run before it is cancelled.</summary>
    public int CommandTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Optional SQL script used to create the SQLite database when the file does not exist yet.
    /// Ignored for PostgreSQL.
    /// </summary>
    public string? SeedScript { get; set; }

    internal TimeSpan CommandTimeout => TimeSpan.FromSeconds(Math.Max(1, CommandTimeoutSeconds));
}
