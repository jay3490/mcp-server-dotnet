using System.Text.RegularExpressions;
using EngineeringMcp.Configuration;
using LibGit2Sharp;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Git;

public sealed record CommitInfo(string Sha, string ShortSha, string Author, DateTimeOffset When, string Subject);

public sealed record FileChange(string Path, string? OldPath, string Status, int Additions, int Deletions, bool IsBinary);

public sealed record DiffSummary(
    string From,
    string To,
    int FilesChanged,
    int Additions,
    int Deletions,
    IReadOnlyList<FileChange> Files,
    bool Truncated);

/// <summary>
/// Read-only views of one configured repository, through LibGit2Sharp rather than a git process,
/// so there is no command line to inject into and no hooks, pagers or external diff drivers run.
/// </summary>
/// <remarks>
/// Revisions are still validated up front: only names, hashes and the <c>~</c>/<c>^</c> ancestry
/// operators are accepted, which rules out ranges, reflog selectors and search expressions such
/// as <c>:/message</c>. Diffs compare two commit trees and never touch the working directory.
/// </remarks>
public sealed partial class GitService(IOptions<GitOptions> options)
{
    private readonly GitOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.RepositoryPath);

    public IReadOnlyList<CommitInfo> GetLog(string? revision, string? path, int? maxCount)
    {
        var take = Math.Clamp(maxCount ?? 20, 1, Math.Max(1, _options.MaxCommits));
        var pathFilter = ValidatePath(path);

        using var repository = Open();
        var start = Resolve(repository, revision ?? "HEAD");
        var filter = new CommitFilter
        {
            IncludeReachableFrom = start,
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
        };

        var commits = pathFilter is null
            ? repository.Commits.QueryBy(filter)
            : repository.Commits.QueryBy(pathFilter, filter).Select(entry => entry.Commit);

        return commits.Take(take).Select(ToInfo).ToList();
    }

    public DiffSummary GetDiffSummary(string? from, string? to)
    {
        using var repository = Open();

        var toCommit = Resolve(repository, to ?? "HEAD");
        var fromCommit = from is null ? toCommit.Parents.FirstOrDefault() : Resolve(repository, from);

        // A root commit is compared with the empty tree, so every file shows as added.
        var compareOptions = new CompareOptions { Similarity = SimilarityOptions.Renames };
        using var patch = repository.Diff.Compare<Patch>(fromCommit?.Tree, toCommit.Tree, compareOptions);

        var all = patch
            .Select(change => new FileChange(
                change.Path,
                change.Status == ChangeKind.Renamed || change.Status == ChangeKind.Copied ? change.OldPath : null,
                change.Status.ToString().ToLowerInvariant(),
                change.LinesAdded,
                change.LinesDeleted,
                change.IsBinaryComparison))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();

        var limit = Math.Max(1, _options.MaxDiffFiles);
        return new DiffSummary(
            From: fromCommit?.Sha ?? "(empty tree)",
            To: toCommit.Sha,
            FilesChanged: all.Count,
            Additions: patch.LinesAdded,
            Deletions: patch.LinesDeleted,
            Files: all.Take(limit).ToList(),
            Truncated: all.Count > limit);
    }

    internal static string ValidateRevision(string revision)
    {
        if (!RevisionPattern().IsMatch(revision) || revision.Contains("..", StringComparison.Ordinal) || revision.EndsWith(".lock", StringComparison.Ordinal))
        {
            throw new ToolErrorException(
                ToolErrorCode.InvalidArgument,
                $"'{revision}' is not an accepted revision. Use a branch, tag or commit hash, optionally followed by ~N or ^N.");
        }

        return revision;
    }

    internal static string? ValidatePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path.Trim().Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/');
        if (normalized.Any(char.IsControl)
            || normalized.Contains(':', StringComparison.Ordinal)
            || segments.Any(s => s is ".." or "." or ""))
        {
            throw new ToolErrorException(ToolErrorCode.InvalidArgument, "The path filter must be a plain path relative to the repository root.");
        }

        return normalized;
    }

    private Repository Open()
    {
        if (!IsConfigured)
        {
            throw new ToolErrorException(ToolErrorCode.Unavailable, "No git repository is configured on this server.");
        }

        var gitDirectory = Repository.Discover(Path.GetFullPath(_options.RepositoryPath));
        if (gitDirectory is null)
        {
            throw new ToolErrorException(ToolErrorCode.Unavailable, "The configured git repository path is not a repository.");
        }

        return new Repository(gitDirectory);
    }

    private static Commit Resolve(Repository repository, string revision)
    {
        ValidateRevision(revision);

        if (repository.Head.Tip is null)
        {
            throw new ToolErrorException(ToolErrorCode.NotFound, "The repository has no commits yet.");
        }

        try
        {
            return repository.Lookup<Commit>(revision)
                ?? throw new ToolErrorException(ToolErrorCode.NotFound, $"Revision '{revision}' does not name a commit.");
        }
        catch (LibGit2SharpException ex)
        {
            throw new ToolErrorException(ToolErrorCode.NotFound, $"Revision '{revision}' could not be resolved: {ex.Message}", ex);
        }
    }

    private static CommitInfo ToInfo(Commit commit) => new(
        commit.Sha,
        commit.Sha[..7],
        commit.Author.Name,
        commit.Author.When,
        commit.MessageShort);

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9._/\-]{0,199}([~^][0-9]{0,4})*$")]
    private static partial Regex RevisionPattern();
}
