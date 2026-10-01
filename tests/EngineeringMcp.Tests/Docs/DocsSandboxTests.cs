using EngineeringMcp.Configuration;
using EngineeringMcp.Docs;
using EngineeringMcp.Tests.Support;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Tests.Docs;

public sealed class DocsSandboxTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _root;

    public DocsSandboxTests()
    {
        _root = _temp.Combine("docs");
        _temp.WriteFile("docs/guide.md", "# Guide\n\nHello.");
        _temp.WriteFile("docs/runbooks/deploy.md", "# Deploy\n\nSteps.");
        _temp.WriteFile("docs/notes.txt", "not markdown");
        _temp.WriteFile("secret.md", "# Secret\n\noutside the root");
        _temp.WriteFile("docs-evil/leak.md", "# Sibling with a shared prefix");
    }

    public void Dispose() => _temp.Dispose();

    private DocsSandbox CreateSandbox(long maxBytes = 256 * 1024) =>
        new(Options.Create(new DocsOptions { Root = _root, MaxDocumentBytes = maxBytes }));

    [Theory]
    [InlineData("guide.md", "guide.md")]
    [InlineData("./guide.md", "guide.md")]
    [InlineData("runbooks/deploy.md", "runbooks/deploy.md")]
    [InlineData("runbooks\\deploy.md", "runbooks/deploy.md")]
    [InlineData("runbooks//deploy.md", "runbooks/deploy.md")]
    [InlineData(" guide.md ", "guide.md")]
    public void Resolves_paths_inside_the_root(string input, string expected)
    {
        var file = CreateSandbox().Resolve(input);

        Assert.StartsWith(_root, file.FullPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expected, file.RelativePath);
    }

    [Theory]
    [InlineData("../secret.md")]
    [InlineData("..\\secret.md")]
    [InlineData("runbooks/../../secret.md")]
    [InlineData("runbooks/../guide.md")]
    [InlineData("..")]
    [InlineData("../docs-evil/leak.md")]
    public void Rejects_parent_directory_segments(string input)
    {
        var error = Assert.Throws<ToolErrorException>(() => CreateSandbox().Resolve(input));

        Assert.Equal(ToolErrorCode.Forbidden, error.Code);
        Assert.Contains("..", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_absolute_paths()
    {
        var absolute = _temp.Combine("secret.md");

        foreach (var input in new[] { absolute, "/etc/passwd", "C:\\Windows\\win.ini", "C:secret.md", "\\\\server\\share\\x.md", "file:///etc/passwd" })
        {
            var error = Assert.Throws<ToolErrorException>(() => CreateSandbox().Resolve(input));
            Assert.Equal(ToolErrorCode.Forbidden, error.Code);
        }
    }

    [Fact]
    public void Rejects_alternate_data_streams()
    {
        var error = Assert.Throws<ToolErrorException>(() => CreateSandbox().Resolve("guide.md:hidden"));

        Assert.Equal(ToolErrorCode.Forbidden, error.Code);
    }

    [Theory]
    [InlineData("guide.md\0.txt")]
    [InlineData("guide\n.md")]
    public void Rejects_control_characters(string input)
    {
        var error = Assert.Throws<ToolErrorException>(() => CreateSandbox().Resolve(input));

        Assert.Equal(ToolErrorCode.InvalidArgument, error.Code);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("runbooks")]
    [InlineData("guide.md.")]
    public void Rejects_files_that_are_not_documents(string input)
    {
        var error = Assert.Throws<ToolErrorException>(() => CreateSandbox().Resolve(input));

        Assert.Equal(ToolErrorCode.Forbidden, error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("./")]
    public void Rejects_empty_paths(string? input)
    {
        var error = Assert.Throws<ToolErrorException>(() => CreateSandbox().Resolve(input));

        Assert.Equal(ToolErrorCode.InvalidArgument, error.Code);
    }

    [Fact]
    public void Missing_documents_are_not_found()
    {
        var error = Assert.Throws<ToolErrorException>(() => CreateSandbox().Resolve("missing.md"));

        Assert.Equal(ToolErrorCode.NotFound, error.Code);
    }

    [Fact]
    public void Rejects_documents_over_the_size_limit()
    {
        var error = Assert.Throws<ToolErrorException>(() => CreateSandbox(maxBytes: 4).Resolve("guide.md"));

        Assert.Equal(ToolErrorCode.InvalidArgument, error.Code);
    }

    [Fact]
    public void Refuses_to_follow_a_linked_directory_that_points_outside()
    {
        FileLinks.CreateDirectoryLink(_temp.Combine("docs", "escape"), _temp.Path);

        var error = Assert.Throws<ToolErrorException>(() => CreateSandbox().Resolve("escape/secret.md"));

        Assert.Equal(ToolErrorCode.Forbidden, error.Code);
        Assert.Contains("links", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_linked_directory_even_when_it_points_inside()
    {
        FileLinks.CreateDirectoryLink(_temp.Combine("docs", "alias"), _temp.Combine("docs", "runbooks"));

        var error = Assert.Throws<ToolErrorException>(() => CreateSandbox().Resolve("alias/deploy.md"));

        Assert.Equal(ToolErrorCode.Forbidden, error.Code);
    }

    [SkippableFact]
    public void Refuses_to_follow_a_file_symlink()
    {
        var created = FileLinks.TryCreateFileSymlink(_temp.Combine("docs", "link.md"), _temp.Combine("secret.md"));
        Skip.IfNot(created, "This account cannot create file symbolic links.");

        var error = Assert.Throws<ToolErrorException>(() => CreateSandbox().Resolve("link.md"));

        Assert.Equal(ToolErrorCode.Forbidden, error.Code);
    }

    [Fact]
    public void Enumeration_lists_documents_and_skips_links()
    {
        FileLinks.CreateDirectoryLink(_temp.Combine("docs", "escape"), _temp.Path);

        var files = CreateSandbox().EnumerateDocuments();

        Assert.Equal(["guide.md", "runbooks/deploy.md"], files.Select(f => f.RelativePath));
    }

    [Fact]
    public void Unconfigured_root_reports_unavailable()
    {
        var sandbox = new DocsSandbox(Options.Create(new DocsOptions { Root = "" }));

        Assert.False(sandbox.IsConfigured);
        Assert.Empty(sandbox.EnumerateDocuments());
        Assert.Equal(ToolErrorCode.Unavailable, Assert.Throws<ToolErrorException>(() => sandbox.Resolve("guide.md")).Code);
    }

    [Fact]
    public async Task Store_reads_text_and_title()
    {
        var store = new DocumentStore(CreateSandbox());

        var doc = await store.ReadAsync("runbooks/deploy.md", CancellationToken.None);

        Assert.Equal("Deploy", doc.Title);
        Assert.Equal("runbooks/deploy.md", doc.Path);
        Assert.Contains("Steps.", doc.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("# Title\nbody", "Title")]
    [InlineData("intro\n\n  # Late title  \n", "Late title")]
    [InlineData("## Only a subheading", "file")]
    public void Title_comes_from_the_first_level_one_heading(string markdown, string expected)
    {
        Assert.Equal(expected, DocumentStore.ExtractTitle(markdown, "dir/file.md"));
    }
}
