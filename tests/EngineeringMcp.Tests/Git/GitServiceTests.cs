using EngineeringMcp.Configuration;
using EngineeringMcp.Git;
using EngineeringMcp.Tests.Support;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Tests.Git;

public sealed class GitServiceTests : IDisposable
{
    private readonly TempGitRepository _repo = new();
    private readonly string[] _shas;

    public GitServiceTests()
    {
        _repo.Write("README.md", "# Demo\n");
        var first = _repo.Commit("docs: add readme");

        _repo.Write("src/app.cs", "class App\n{\n}\n");
        _repo.Write("README.md", "# Demo\n\nNow with an app.\n");
        var second = _repo.Commit("feat: add app", author: "Sam Reviewer");

        _repo.Move("src/app.cs", "src/program.cs");
        _repo.Write("notes.txt", "line one\nline two\n");
        var third = _repo.Commit("refactor: rename app\n\nLonger body that is not part of the subject.");

        _repo.Delete("notes.txt");
        _repo.Write("src/program.cs", "class App\n{\n    static void Main() { }\n}\n");
        var fourth = _repo.Commit("feat: add entry point");

        _shas = [first, second, third, fourth];
    }

    public void Dispose() => _repo.Dispose();

    private GitService CreateService(int maxCommits = 50, int maxDiffFiles = 200) =>
        new(Options.Create(new GitOptions { RepositoryPath = _repo.Path, MaxCommits = maxCommits, MaxDiffFiles = maxDiffFiles }));

    [Fact]
    public void Log_lists_commits_newest_first()
    {
        var log = CreateService().GetLog(revision: null, path: null, maxCount: null);

        Assert.Equal(_shas.Reverse(), log.Select(c => c.Sha));
        Assert.Equal("feat: add entry point", log[0].Subject);
        Assert.Equal("refactor: rename app", log[1].Subject);
        Assert.Equal("Sam Reviewer", log[2].Author);
        Assert.Equal(log[0].Sha[..7], log[0].ShortSha);
    }

    [Fact]
    public void Log_honours_the_requested_count_and_the_configured_cap()
    {
        Assert.Equal(2, CreateService().GetLog(null, null, maxCount: 2).Count);
        Assert.Equal(3, CreateService(maxCommits: 3).GetLog(null, null, maxCount: 100).Count);
    }

    [Fact]
    public void Log_starts_from_the_given_revision()
    {
        var log = CreateService().GetLog("HEAD~2", null, null);

        Assert.Equal([_shas[1], _shas[0]], log.Select(c => c.Sha));
    }

    [Fact]
    public void Log_accepts_an_abbreviated_hash()
    {
        var log = CreateService().GetLog(_shas[1][..8], null, null);

        Assert.Equal(_shas[1], log[0].Sha);
    }

    [Fact]
    public void Log_filters_by_path()
    {
        var log = CreateService().GetLog(null, "README.md", null);

        Assert.Equal([_shas[1], _shas[0]], log.Select(c => c.Sha));
    }

    [Theory]
    [InlineData("HEAD..main")]
    [InlineData("HEAD@{1}")]
    [InlineData(":/fix")]
    [InlineData("--output=/tmp/x")]
    [InlineData("-n1")]
    [InlineData("HEAD; rm -rf /")]
    [InlineData("main.lock")]
    [InlineData("")]
    public void Rejects_revision_syntax_beyond_names_and_ancestry(string revision)
    {
        var error = Assert.Throws<ToolErrorException>(() => CreateService().GetLog(revision, null, null));

        Assert.Equal(ToolErrorCode.InvalidArgument, error.Code);
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("HEAD~1")]
    [InlineData("HEAD^")]
    [InlineData("HEAD~1^1")]
    [InlineData("release/1.2")]
    [InlineData("v1.0.0")]
    public void Accepts_simple_revisions(string revision)
    {
        Assert.Equal(revision, GitService.ValidateRevision(revision));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("src/../../x")]
    [InlineData("C:/Windows")]
    [InlineData("a//b")]
    public void Rejects_path_filters_that_are_not_plain_relative_paths(string path)
    {
        var error = Assert.Throws<ToolErrorException>(() => CreateService().GetLog(null, path, null));

        Assert.Equal(ToolErrorCode.InvalidArgument, error.Code);
    }

    [Fact]
    public void Unknown_revisions_are_not_found()
    {
        var error = Assert.Throws<ToolErrorException>(() => CreateService().GetLog("does-not-exist", null, null));

        Assert.Equal(ToolErrorCode.NotFound, error.Code);
    }

    [Fact]
    public void Diff_summary_defaults_to_the_last_commit()
    {
        var diff = CreateService().GetDiffSummary(from: null, to: null);

        Assert.Equal(_shas[2], diff.From);
        Assert.Equal(_shas[3], diff.To);
        Assert.Equal(2, diff.FilesChanged);

        var deleted = diff.Files.Single(f => f.Path == "notes.txt");
        Assert.Equal("deleted", deleted.Status);
        Assert.Equal(2, deleted.Deletions);

        var modified = diff.Files.Single(f => f.Path == "src/program.cs");
        Assert.Equal("modified", modified.Status);
        Assert.Equal(1, modified.Additions);
        Assert.Equal(diff.Files.Sum(f => f.Additions), diff.Additions);
        Assert.Equal(diff.Files.Sum(f => f.Deletions), diff.Deletions);
    }

    [Fact]
    public void Diff_summary_compares_two_revisions()
    {
        var diff = CreateService().GetDiffSummary(_shas[0], _shas[2]);

        Assert.Equal(["README.md", "notes.txt", "src/program.cs"], diff.Files.Select(f => f.Path));
        Assert.Equal("modified", diff.Files[0].Status);
        Assert.Equal("added", diff.Files[1].Status);
    }

    [Fact]
    public void Diff_summary_detects_renames()
    {
        var diff = CreateService().GetDiffSummary(_shas[1], _shas[2]);

        var renamed = diff.Files.Single(f => f.Status == "renamed");
        Assert.Equal("src/program.cs", renamed.Path);
        Assert.Equal("src/app.cs", renamed.OldPath);
    }

    [Fact]
    public void Diff_of_the_root_commit_is_against_the_empty_tree()
    {
        var diff = CreateService().GetDiffSummary(null, _shas[0]);

        Assert.Equal("(empty tree)", diff.From);
        Assert.Equal("added", Assert.Single(diff.Files).Status);
    }

    [Fact]
    public void Diff_file_list_is_capped_but_totals_are_not()
    {
        var diff = CreateService(maxDiffFiles: 1).GetDiffSummary(_shas[0], _shas[2]);

        Assert.Single(diff.Files);
        Assert.Equal(3, diff.FilesChanged);
        Assert.True(diff.Truncated);
    }

    [Fact]
    public void Unconfigured_repository_is_unavailable()
    {
        var service = new GitService(Options.Create(new GitOptions()));

        Assert.Equal(ToolErrorCode.Unavailable, Assert.Throws<ToolErrorException>(() => service.GetLog(null, null, null)).Code);
    }

    [Fact]
    public void A_folder_that_is_not_a_repository_is_unavailable()
    {
        using var empty = new TempDirectory();
        var service = new GitService(Options.Create(new GitOptions { RepositoryPath = empty.Path }));

        Assert.Equal(ToolErrorCode.Unavailable, Assert.Throws<ToolErrorException>(() => service.GetDiffSummary(null, null)).Code);
    }
}
