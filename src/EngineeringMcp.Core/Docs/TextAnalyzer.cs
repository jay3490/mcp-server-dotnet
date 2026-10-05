using System.Text;
using System.Text.RegularExpressions;

namespace EngineeringMcp.Docs;

/// <summary>
/// Turns markdown into plain text and plain text into search terms. Indexing, querying and
/// snippet highlighting all go through <see cref="Normalize"/>, so a highlighted word is always
/// one that actually matched.
/// </summary>
public static partial class TextAnalyzer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "has", "have", "how", "i",
        "if", "in", "is", "it", "its", "of", "on", "or", "that", "the", "this", "to", "was", "what",
        "when", "where", "which", "who", "will", "with", "you", "your",
    };

    /// <summary>Lower-cases a word and strips a plural "s" so "deployments" matches "deployment".</summary>
    /// <returns>The term, or <see langword="null"/> for stop words and single characters.</returns>
    public static string? Normalize(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        var lower = word.ToLowerInvariant();
        if (lower.Length < 2 || StopWords.Contains(lower))
        {
            return null;
        }

        if (lower.Length > 4 && lower.EndsWith('s') && !lower.EndsWith("ss", StringComparison.Ordinal))
        {
            lower = lower[..^1];
        }

        return lower;
    }

    /// <summary>Search terms in the order they appear, duplicates included.</summary>
    public static IEnumerable<string> Terms(string text)
    {
        foreach (Match match in WordPattern().Matches(text))
        {
            if (Normalize(match.Value) is { } term)
            {
                yield return term;
            }
        }
    }

    /// <summary>
    /// Removes the markdown syntax that would clutter a snippet: heading markers, emphasis,
    /// inline code ticks, table pipes, block quotes and link targets. Underscores stay, because
    /// identifiers such as <c>rolled_back</c> are more common in engineering docs than underscore emphasis.
    /// </summary>
    public static string MarkdownToPlainText(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var text = FencePattern().Replace(markdown, " ");
        text = LinkPattern().Replace(text, "$1");
        text = TableRulePattern().Replace(text, " ");

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '*' or '`':
                    // Emphasis and code markers hug the word, so drop them without leaving a gap.
                    break;
                case '#' or '|' or '>':
                    builder.Append(' ');
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return WhitespacePattern().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    internal static partial Regex WordPattern();

    [GeneratedRegex(@"```.*?```", RegexOptions.Singleline)]
    private static partial Regex FencePattern();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?")]
    private static partial Regex TableRulePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
