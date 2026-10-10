using System.ComponentModel;
using System.Globalization;
using System.Text;
using EngineeringMcp.Docs;
using EngineeringMcp.Server;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EngineeringMcp.Tools;

[McpServerToolType]
public sealed class DocsTools(DocsSearchService search, DocumentStore store)
{
    [McpServerTool(Name = ToolNames.SearchDocs, Title = "Search engineering docs", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Full-text search (BM25) over the engineering markdown docs: runbooks, ADRs and guides. Returns the best " +
        "matches with a snippet in which matching words are in **bold**. Use read_doc with a returned path to read a document.")]
    public CallToolResult Search(
        [Description("Words to search for, e.g. 'database failover connection pool'.")] string query,
        [Description("How many results to return (default 5).")] int? limit = null)
    {
        return ToolResults.Run(() =>
        {
            var hits = search.Search(query, limit);
            if (hits.Count == 0)
            {
                return ToolResults.Success($"No documents match '{query}'.", new { query, results = hits });
            }

            var text = new StringBuilder();
            for (var i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                text.Append(CultureInfo.InvariantCulture, $"{i + 1}. **{hit.Title}** (`{hit.Path}`, score {hit.Score:0.###})\n   {hit.Snippet}\n");
            }

            return ToolResults.Success(text.ToString(), new { query, results = hits });
        });
    }

    [McpServerTool(Name = ToolNames.ReadDoc, Title = "Read an engineering doc", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Returns the markdown of one document, addressed by its path relative to the docs root (as returned by search_docs).")]
    public Task<CallToolResult> ReadAsync(
        [Description("Relative path such as 'runbooks/deploying.md'.")] string path,
        CancellationToken cancellationToken = default)
    {
        return ToolResults.RunAsync(async () =>
        {
            var doc = await store.ReadAsync(path, cancellationToken).ConfigureAwait(false);
            return ToolResults.Success(doc.Text, new
            {
                path = doc.Path,
                title = doc.Title,
                length = doc.Length,
                lastModifiedUtc = doc.LastWriteTimeUtc,
                uri = DocUris.ToUri(doc.Path),
            });
        });
    }
}
