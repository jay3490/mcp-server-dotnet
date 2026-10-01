using EngineeringMcp.Docs;

namespace EngineeringMcp.Tests.Docs;

public class Bm25IndexTests
{
    private static IndexedDocument Doc(string path, string body, string title = "") => new(path, title, body);

    private static string[] Terms(string query) => TextAnalyzer.Terms(query).Distinct().ToArray();

    [Fact]
    public void Score_matches_the_bm25_formula()
    {
        var a = Doc("a.md", "cache cache invalidation");
        var b = Doc("b.md", "deploy pipeline rollback steps");
        var index = new Bm25Index([a, b]);

        // N = 2, n(cache) = 1, f(cache, a) = 2, |a| = 3, avgdl = (3 + 4) / 2 = 3.5
        var idf = Math.Log(1 + ((2 - 1 + 0.5) / (1 + 0.5)));
        var expected = idf * (2 * 2.2) / (2 + (1.2 * (1 - 0.75 + (0.75 * 3 / 3.5))));

        Assert.Equal(expected, index.Score(a, ["cache"]), precision: 10);
        Assert.Equal(0, index.Score(b, ["cache"]));
    }

    [Fact]
    public void Rare_terms_outweigh_common_ones()
    {
        var index = new Bm25Index(
        [
            Doc("common.md", "service service deploy"),
            Doc("rare.md", "service failover"),
            Doc("other.md", "service metrics"),
        ]);

        var results = index.Search(Terms("service failover"), 3);

        Assert.Equal("rare.md", results[0].Document.Path);
    }

    [Fact]
    public void Higher_term_frequency_ranks_higher_at_equal_length()
    {
        var index = new Bm25Index(
        [
            Doc("once.md", "rollback alpha beta gamma"),
            Doc("twice.md", "rollback rollback beta gamma"),
            Doc("none.md", "alpha beta gamma delta"),
        ]);

        var results = index.Search(Terms("rollback"), 10);

        Assert.Equal(["twice.md", "once.md"], results.Select(r => r.Document.Path));
    }

    [Fact]
    public void Shorter_documents_win_at_equal_term_frequency()
    {
        var index = new Bm25Index(
        [
            Doc("long.md", "pool " + string.Join(' ', Enumerable.Repeat("filler", 50))),
            Doc("short.md", "pool sizing"),
            Doc("unrelated.md", "alpha"),
        ]);

        var results = index.Search(Terms("pool"), 10);

        Assert.Equal("short.md", results[0].Document.Path);
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public void Term_frequency_saturates()
    {
        var spam = Doc("spam.md", string.Join(' ', Enumerable.Repeat("incident", 100)));
        var index = new Bm25Index([spam, Doc("balanced.md", "incident review timeline"), Doc("unrelated.md", "alpha beta")]);

        // However often a term repeats, its contribution stays below idf * (k1 + 1).
        Assert.True(index.Score(spam, ["incident"]) < index.InverseDocumentFrequency("incident") * 2.2);
    }

    [Fact]
    public void Title_terms_are_boosted()
    {
        var index = new Bm25Index(
        [
            Doc("body.md", "notes about failover and other topics here", title: "Notes"),
            Doc("title.md", "steps and other topics written here today", title: "Failover"),
            Doc("unrelated.md", "alpha beta", title: "Other"),
        ]);

        var results = index.Search(Terms("failover"), 10);

        Assert.Equal("title.md", results[0].Document.Path);
    }

    [Fact]
    public void Documents_without_matches_are_left_out()
    {
        var index = new Bm25Index([Doc("a.md", "alpha"), Doc("b.md", "beta")]);

        Assert.Empty(index.Search(Terms("gamma"), 10));
    }

    [Fact]
    public void Ties_are_broken_by_path()
    {
        var index = new Bm25Index([Doc("b.md", "same words"), Doc("a.md", "same words"), Doc("c.md", "other")]);

        var results = index.Search(Terms("same"), 10);

        Assert.Equal(["a.md", "b.md"], results.Select(r => r.Document.Path));
    }

    [Fact]
    public void Empty_index_returns_nothing()
    {
        Assert.Empty(new Bm25Index([]).Search(["anything"], 5));
    }
}
