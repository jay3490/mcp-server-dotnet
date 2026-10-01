using EngineeringMcp.Configuration;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Docs;

public sealed record SearchHit(string Path, string Title, double Score, string Snippet);

/// <summary>
/// BM25 search over the docs folder. The index is rebuilt lazily whenever the set of files, their
/// sizes or their modification times change, so edits show up on the next search.
/// </summary>
public sealed class DocsSearchService(DocsSandbox sandbox, IOptions<DocsOptions> options)
{
    private readonly object _gate = new();
    private Bm25Index? _index;
    private string? _fingerprint;

    public IReadOnlyList<SearchHit> Search(string? query, int? limit)
    {
        if (!sandbox.IsConfigured)
        {
            throw new ToolErrorException(ToolErrorCode.Unavailable, "No docs folder is configured on this server.");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ToolErrorException(ToolErrorCode.InvalidArgument, "A search query is required.");
        }

        var terms = TextAnalyzer.Terms(query).Distinct(StringComparer.Ordinal).ToList();
        if (terms.Count == 0)
        {
            throw new ToolErrorException(ToolErrorCode.InvalidArgument, "The query has no searchable words (only stop words or punctuation).");
        }

        var max = Math.Max(1, options.Value.MaxSearchResults);
        var take = Math.Clamp(limit ?? 5, 1, max);

        return GetIndex()
            .Search(terms, take)
            .Select(r => new SearchHit(
                r.Document.Path,
                r.Document.Title,
                Math.Round(r.Score, 3),
                SnippetBuilder.Build(r.Document.PlainText, terms)))
            .ToList();
    }

    private Bm25Index GetIndex()
    {
        var files = sandbox.EnumerateDocuments();
        var fingerprint = string.Join(
            '\n',
            files.Select(f => $"{f.RelativePath}|{f.Length}|{f.LastWriteTimeUtc.Ticks}"));

        lock (_gate)
        {
            if (_index is not null && fingerprint == _fingerprint)
            {
                return _index;
            }

            var documents = new List<IndexedDocument>(files.Count);
            foreach (var file in files)
            {
                var markdown = File.ReadAllText(file.FullPath);
                documents.Add(new IndexedDocument(
                    file.RelativePath,
                    DocumentStore.ExtractTitle(markdown, file.RelativePath),
                    TextAnalyzer.MarkdownToPlainText(markdown)));
            }

            _index = new Bm25Index(documents);
            _fingerprint = fingerprint;
            return _index;
        }
    }
}
