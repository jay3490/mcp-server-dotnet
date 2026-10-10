using System.ComponentModel;
using System.Globalization;
using System.Text;
using EngineeringMcp.Formatting;
using EngineeringMcp.Git;
using EngineeringMcp.Sql;
using EngineeringMcp.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EngineeringMcp.Server;

/// <summary>
/// Reusable prompt templates. Each one gathers the relevant data on the server and hands the
/// model a single, self-contained request, so the client does not have to chain tool calls.
/// </summary>
[McpServerPromptType]
public sealed class EngineeringPrompts(SqlSchemaService schema, GitService git)
{
    public const string ExplainSchemaName = "explain_schema";
    public const string SummariseRecentChangesName = "summarise_recent_changes";

    [McpServerPrompt(Name = ExplainSchemaName, Title = "Explain the database schema")]
    [Description("Explains what the tables in the engineering database hold and how they relate, with example questions they can answer.")]
    public async Task<GetPromptResult> ExplainSchemaAsync(
        [Description("Optional table or view to focus on.")] string? table = null,
        CancellationToken cancellationToken = default)
    {
        var tables = await Run(() => schema.GetSchemaAsync(table, cancellationToken)).ConfigureAwait(false);
        if (tables.Count == 0)
        {
            throw new McpProtocolException(
                table is null ? "The database has no tables." : $"No table or view named '{table}'.",
                McpErrorCode.InvalidParams);
        }

        var focus = table is null ? "this database schema" : $"the `{table}` table in this database";
        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Explain {focus} to an engineer who is new to the team.\n\n")
            .Append("Cover what each table stores, how the tables relate to each other (infer foreign keys from column names), ")
            .Append("and suggest three questions the data can answer, each with a SQL query that would pass the read-only sql_query tool.\n\n")
            .Append(SqlTools.Describe(schema.Dialect, tables))
            .ToString();

        return new GetPromptResult
        {
            Description = $"Explain {focus}",
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = text } }],
        };
    }

    [McpServerPrompt(Name = SummariseRecentChangesName, Title = "Summarise recent changes")]
    [Description("Summarises recent commits of the configured repository for a status update or a release note.")]
    public GetPromptResult SummariseRecentChanges(
        [Description("How many recent commits to include (default 10).")] int? count = null,
        [Description("Branch, tag or commit to start from. Defaults to HEAD.")] string? revision = null)
    {
        var commits = Run(() => git.GetLog(revision, null, count ?? 10));
        if (commits.Count == 0)
        {
            throw new McpProtocolException("No commits found.", McpErrorCode.InvalidParams);
        }

        var text = new StringBuilder()
            .Append("Summarise the recent changes below for a team status update. Group related commits, call out anything ")
            .Append("risky (migrations, configuration, security) and keep it under 200 words.\n\n")
            .Append(CultureInfo.InvariantCulture, $"## Commits ({commits.Count}, newest first)\n\n")
            .Append(MarkdownTable.Render(
                ["commit", "date", "author", "subject"],
                commits.Select(c => (IReadOnlyList<string?>)
                [
                    c.ShortSha,
                    c.When.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    c.Author,
                    c.Subject,
                ])));

        if (commits.Count > 1)
        {
            var diff = Run(() => git.GetDiffSummary(commits[^1].Sha, commits[0].Sha));
            text.Append(CultureInfo.InvariantCulture, $"\n## Files changed after {commits[^1].ShortSha} up to {commits[0].ShortSha}\n\n")
                .Append(CultureInfo.InvariantCulture, $"{diff.FilesChanged} file(s), +{diff.Additions} -{diff.Deletions}\n\n")
                .Append(MarkdownTable.Render(
                    ["status", "path", "+", "-"],
                    diff.Files.Select(f => (IReadOnlyList<string?>)
                    [
                        f.Status,
                        f.Path,
                        f.Additions.ToString(CultureInfo.InvariantCulture),
                        f.Deletions.ToString(CultureInfo.InvariantCulture),
                    ])));
        }

        return new GetPromptResult
        {
            Description = $"Summarise the last {commits.Count} commit(s)",
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = text.ToString() } }],
        };
    }

    /// <summary>Prompts have no isError result, so expected failures become protocol errors with the same message.</summary>
    private static T Run<T>(Func<T> body)
    {
        try
        {
            return body();
        }
        catch (ToolErrorException ex)
        {
            throw new McpProtocolException(ex.Message, ex, McpErrorCode.InvalidParams);
        }
    }

    private static async Task<T> Run<T>(Func<Task<T>> body)
    {
        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (ToolErrorException ex)
        {
            throw new McpProtocolException(ex.Message, ex, McpErrorCode.InvalidParams);
        }
    }
}
