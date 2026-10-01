namespace EngineeringMcp.Docs;

/// <summary>A document prepared for ranking.</summary>
public sealed class IndexedDocument
{
    public IndexedDocument(string path, string title, string plainText, int titleWeight = 3)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(plainText);

        Path = path;
        Title = title;
        PlainText = plainText;

        // A simple field boost: every title term counts as if it appeared titleWeight times.
        var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
        var length = 0;
        foreach (var term in TextAnalyzer.Terms(plainText))
        {
            frequencies[term] = frequencies.GetValueOrDefault(term) + 1;
            length++;
        }

        foreach (var term in TextAnalyzer.Terms(title))
        {
            frequencies[term] = frequencies.GetValueOrDefault(term) + titleWeight;
            length += titleWeight;
        }

        TermFrequencies = frequencies;
        Length = length;
    }

    public string Path { get; }

    public string Title { get; }

    public string PlainText { get; }

    public IReadOnlyDictionary<string, int> TermFrequencies { get; }

    /// <summary>Number of terms after stop-word removal, including the title boost.</summary>
    public int Length { get; }
}

/// <summary>
/// Okapi BM25 over an in-memory set of documents.
/// </summary>
/// <remarks>
/// score(D, Q) = sum over terms t in Q of
///   idf(t) * f(t, D) * (k1 + 1) / (f(t, D) + k1 * (1 - b + b * |D| / avgdl))
/// with idf(t) = ln(1 + (N - n(t) + 0.5) / (n(t) + 0.5)), which stays positive even for terms
/// that appear in most documents.
/// </remarks>
public sealed class Bm25Index
{
    private readonly IReadOnlyList<IndexedDocument> _documents;
    private readonly Dictionary<string, int> _documentFrequency = new(StringComparer.Ordinal);
    private readonly double _averageLength;
    private readonly double _k1;
    private readonly double _b;

    public Bm25Index(IEnumerable<IndexedDocument> documents, double k1 = 1.2, double b = 0.75)
    {
        ArgumentNullException.ThrowIfNull(documents);

        _documents = documents.ToList();
        _k1 = k1;
        _b = b;

        foreach (var document in _documents)
        {
            foreach (var term in document.TermFrequencies.Keys)
            {
                _documentFrequency[term] = _documentFrequency.GetValueOrDefault(term) + 1;
            }
        }

        _averageLength = _documents.Count == 0 ? 0 : _documents.Average(d => d.Length);
    }

    public int Count => _documents.Count;

    public double InverseDocumentFrequency(string term)
    {
        var n = _documentFrequency.GetValueOrDefault(term);
        return Math.Log(1 + ((_documents.Count - n + 0.5) / (n + 0.5)));
    }

    public double Score(IndexedDocument document, IEnumerable<string> queryTerms)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(queryTerms);

        var score = 0.0;
        var lengthRatio = _averageLength == 0 ? 0 : document.Length / _averageLength;

        foreach (var term in queryTerms.Distinct(StringComparer.Ordinal))
        {
            var frequency = document.TermFrequencies.GetValueOrDefault(term);
            if (frequency == 0)
            {
                continue;
            }

            var numerator = frequency * (_k1 + 1);
            var denominator = frequency + (_k1 * (1 - _b + (_b * lengthRatio)));
            score += InverseDocumentFrequency(term) * numerator / denominator;
        }

        return score;
    }

    /// <summary>Documents with a positive score, best first; ties are broken by path.</summary>
    public IReadOnlyList<(IndexedDocument Document, double Score)> Search(IReadOnlyCollection<string> queryTerms, int limit)
    {
        ArgumentNullException.ThrowIfNull(queryTerms);

        return _documents
            .Select(d => (Document: d, Score: Score(d, queryTerms)))
            .Where(r => r.Score > 0)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Document.Path, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }
}
