using System.ComponentModel;
using System.Globalization;
using System.Text;
using EngineeringMcp.Formatting;
using EngineeringMcp.Git;
using EngineeringMcp.Server;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EngineeringMcp.Tools;

[McpServerToolType]
public sealed class GitTools(GitService git)
{
    [McpServerTool(Name = ToolNames.GitLog, Title = "Show recent commits", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Lists commits of the configured repository, newest first, optionally starting from a revision and limited to a path.")]
    public CallToolResult Log(
        [Description("Branch, tag or commit hash to start from, optionally with ~N or ^N. Defaults to HEAD.")] string? revision = null,
        [Description("Only commits that touch this path (relative to the repository root).")] string? path = null,
        [Description("How many commits to return (default 20, capped by the server).")] int? maxCount = null)
    {
        return ToolResults.Run(() =>
        {
            var commits = git.GetLog(revision, path, maxCount);
            var text = MarkdownTable.Render(
                ["commit", "date (UTC)", "author", "subject"],
                commits.Select(c => (IReadOnlyList<string?>)
                [
                    c.ShortSha,
                    c.When.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    c.Author,
                    c.Subject,
                ]));

            return ToolResults.Success($"{text}\n{commits.Count} commit(s).", new { commits });
        });
    }

    [McpServerTool(Name = ToolNames.GitDiffSummary, Title = "Summarise changes between commits", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Summarises what changed between two commits: files added, modified, deleted or renamed with line counts. " +
        "Defaults to the last commit compared with its parent. Does not return file contents.")]
    public CallToolResult DiffSummary(
        [Description("Older revision. Defaults to the first parent of 'to'.")] string? from = null,
        [Description("Newer revision. Defaults to HEAD.")] string? to = null)
    {
        return ToolResults.Run(() =>
        {
            var diff = git.GetDiffSummary(from, to);

            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture, $"{Short(diff.From)}..{Short(diff.To)}: {diff.FilesChanged} file(s) changed, +{diff.Additions} -{diff.Deletions}\n\n");
            text.Append(MarkdownTable.Render(
                ["status", "path", "+", "-"],
                diff.Files.Select(f => (IReadOnlyList<string?>)
                [
                    f.Status,
                    f.OldPath is null ? f.Path : $"{f.OldPath} -> {f.Path}",
                    f.IsBinary ? "bin" : f.Additions.ToString(CultureInfo.InvariantCulture),
                    f.IsBinary ? "bin" : f.Deletions.ToString(CultureInfo.InvariantCulture),
                ])));

            if (diff.Truncated)
            {
                text.Append(CultureInfo.InvariantCulture, $"\nOnly the first {diff.Files.Count} files are listed; the totals cover all of them.\n");
            }

            return ToolResults.Success(text.ToString(), diff);
        });
    }

    private static string Short(string sha) => sha.Length == 40 ? sha[..7] : sha;
}
