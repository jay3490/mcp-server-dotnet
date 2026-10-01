using System.Text;

namespace EngineeringMcp.Docs;

/// <summary>
/// Picks the stretch of a document that best covers the query and marks matching words in
/// markdown bold.
/// </summary>
public static class SnippetBuilder
{
    public const char Ellipsis = '…';

    /// <param name="plainText">Document text with markdown syntax removed.</param>
    /// <param name="queryTerms">Normalised query terms.</param>
    /// <param name="windowWords">How many words the snippet spans.</param>
    public static string Build(string plainText, IReadOnlyCollection<string> queryTerms, int windowWords = 30)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        ArgumentNullException.ThrowIfNull(queryTerms);

        var words = TextAnalyzer.WordPattern().Matches(plainText);
        if (words.Count == 0)
        {
            return string.Empty;
        }

        var terms = new HashSet<string>(queryTerms, StringComparer.Ordinal);
        var matchTerms = new string?[words.Count];
        for (var i = 0; i < words.Count; i++)
        {
            var term = TextAnalyzer.Normalize(words[i].Value);
            matchTerms[i] = term is not null && terms.Contains(term) ? term : null;
        }

        var window = Math.Min(windowWords, words.Count);
        var bestStart = 0;
        (int Distinct, int Total) best = (-1, -1);

        // Prefer windows that cover more distinct query terms, then more hits overall; the
        // earliest such window wins ties so snippets favour the start of the document.
        for (var start = 0; start + window <= words.Count; start++)
        {
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            var total = 0;
            for (var i = start; i < start + window; i++)
            {
                if (matchTerms[i] is { } term)
                {
                    distinct.Add(term);
                    total++;
                }
            }

            if (distinct.Count > best.Distinct || (distinct.Count == best.Distinct && total > best.Total))
            {
                best = (distinct.Count, total);
                bestStart = start;
            }
        }

        var end = bestStart + window - 1;
        var builder = new StringBuilder();
        if (bestStart > 0)
        {
            builder.Append(Ellipsis);
        }

        for (var i = bestStart; i <= end; i++)
        {
            if (i > bestStart)
            {
                var gapStart = words[i - 1].Index + words[i - 1].Length;
                builder.Append(plainText, gapStart, words[i].Index - gapStart);
            }

            if (matchTerms[i] is not null)
            {
                builder.Append("**").Append(words[i].Value).Append("**");
            }
            else
            {
                builder.Append(words[i].Value);
            }
        }

        var lastWordEnd = words[end].Index + words[end].Length;
        if (end < words.Count - 1)
        {
            builder.Append(Ellipsis);
        }
        else if (lastWordEnd < plainText.Length)
        {
            builder.Append(plainText.AsSpan(lastWordEnd).TrimEnd());
        }

        return builder.ToString();
    }
}
