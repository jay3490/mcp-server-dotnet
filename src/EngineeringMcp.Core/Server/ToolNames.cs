namespace EngineeringMcp.Server;

/// <summary>The tool names exposed over MCP; these are also the keys used in configuration.</summary>
public static class ToolNames
{
    public const string SqlQuery = "sql_query";
    public const string SqlSchema = "sql_schema";
    public const string SearchDocs = "search_docs";
    public const string ReadDoc = "read_doc";
    public const string GitLog = "git_log";
    public const string GitDiffSummary = "git_diff_summary";
    public const string HttpStatus = "http_status";

    public static IReadOnlyList<string> All { get; } =
        [SqlQuery, SqlSchema, SearchDocs, ReadDoc, GitLog, GitDiffSummary, HttpStatus];
}
