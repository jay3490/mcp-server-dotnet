using EngineeringMcp.Docs;

namespace EngineeringMcp.Tests.Docs;

public class SnippetAndAnalyzerTests
{
    [Theory]
    [InlineData("Deployments", "deployment")]
    [InlineData("CLASS", "class")]
    [InlineData("bus", "bus")]
    [InlineData("the", null)]
    [InlineData("a", null)]
    [InlineData("v2", "v2")]
    public void Normalizes_words(string word, string? expected)
    {
        Assert.Equal(expected, TextAnalyzer.Normalize(word));
    }

    [Fact]
    public void Splits_identifiers_into_terms()
    {
        Assert.Equal(["api", "gateway", "rolled", "back"], TextAnalyzer.Terms("the api-gateway was rolled_back"));
    }

    [Fact]
    public void Strips_markdown_syntax()
    {
        const string markdown = "# Title\n\n| a | b |\n|---|---|\n| **bold** | `code` |\n\nSee [the runbook](runbooks/x.md).\n\n```\nignored code\n```";

        var text = TextAnalyzer.MarkdownToPlainText(markdown);

        Assert.Equal("Title a b bold code See the runbook.", text);
    }

    [Fact]
    public void Emphasis_markers_do_not_leave_gaps_before_punctuation()
    {
        Assert.Equal("the billing, ledger and flags.", TextAnalyzer.MarkdownToPlainText("the **billing**, `ledger` and *flags*."));
    }

    [Fact]
    public void Highlights_every_matching_word()
    {
        var snippet = SnippetBuilder.Build("Roll back the deploy. Rollbacks are fast.", ["rollback", "deploy"]);

        Assert.Equal("Roll back the **deploy**. **Rollbacks** are fast.", snippet);
    }

    [Fact]
    public void Centres_the_window_on_the_densest_matches()
    {
        var filler = string.Join(' ', Enumerable.Range(0, 40).Select(i => $"w{i}"));
        var text = $"{filler} connection pool sizing matters {filler}";

        var snippet = SnippetBuilder.Build(text, ["connection", "pool"], windowWords: 6);

        Assert.Equal(SnippetBuilder.Ellipsis, snippet[0]);
        Assert.Equal(SnippetBuilder.Ellipsis, snippet[^1]);
        Assert.Contains("**connection** **pool**", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void Prefers_windows_that_cover_more_distinct_terms()
    {
        var text = "cache cache cache cache x x x x x x x x x x cache ttl";

        var snippet = SnippetBuilder.Build(text, ["cache", "ttl"], windowWords: 3);

        Assert.Contains("**ttl**", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void Falls_back_to_the_opening_words_without_matches()
    {
        var snippet = SnippetBuilder.Build("one two three four", ["zebra"], windowWords: 2);

        Assert.Equal($"one two{SnippetBuilder.Ellipsis}", snippet);
    }

    [Fact]
    public void Empty_text_gives_an_empty_snippet()
    {
        Assert.Equal(string.Empty, SnippetBuilder.Build("", ["x"]));
    }
}
