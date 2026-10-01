using EngineeringMcp.Configuration;
using EngineeringMcp.Docs;
using EngineeringMcp.Tests.Support;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Tests.Docs;

public sealed class DocsSearchServiceTests : IDisposable
{
    private static readonly string SampleDocs = Path.Combine(AppContext.BaseDirectory, "samples", "docs");

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static DocsSearchService CreateService(string root, int maxResults = 20)
    {
        var options = Options.Create(new DocsOptions { Root = root, MaxSearchResults = maxResults });
        return new DocsSearchService(new DocsSandbox(options), options);
    }

    [Fact]
    public void Finds_the_failover_runbook_in_the_sample_docs()
    {
        var hits = CreateService(SampleDocs).Search("promote the standby during failover", limit: 3);

        Assert.Equal("runbooks/database-failover.md", hits[0].Path);
        Assert.Equal("Runbook: PostgreSQL failover", hits[0].Title);
        Assert.Contains("**", hits[0].Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void Plural_and_singular_forms_match()
    {
        var hits = CreateService(SampleDocs).Search("rollbacks", limit: 5);

        Assert.Contains(hits, h => h.Path == "runbooks/deploying.md");
    }

    [Fact]
    public void Matches_inside_hyphenated_names()
    {
        var hits = CreateService(SampleDocs).Search("gateway", limit: 5);

        Assert.Equal("architecture.md", hits[0].Path);
    }

    [Fact]
    public void Limit_is_clamped_to_the_configured_maximum()
    {
        var hits = CreateService(SampleDocs, maxResults: 2).Search("service", limit: 50);

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void Stop_word_only_queries_are_rejected()
    {
        var error = Assert.Throws<ToolErrorException>(() => CreateService(SampleDocs).Search("the and of", null));

        Assert.Equal(ToolErrorCode.InvalidArgument, error.Code);
    }

    [Fact]
    public void Index_picks_up_new_and_changed_files()
    {
        var root = _temp.Combine("docs");
        _temp.WriteFile("docs/a.md", "# Alpha\n\nnothing to see");
        var service = CreateService(root);

        Assert.Empty(service.Search("kubernetes", null));

        _temp.WriteFile("docs/b.md", "# Cluster\n\nkubernetes upgrade notes");
        Assert.Equal("b.md", Assert.Single(service.Search("kubernetes", null)).Path);
    }

    [Fact]
    public void Unconfigured_docs_report_unavailable()
    {
        var error = Assert.Throws<ToolErrorException>(() => CreateService("").Search("anything", null));

        Assert.Equal(ToolErrorCode.Unavailable, error.Code);
    }
}
