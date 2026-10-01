namespace EngineeringMcp.Configuration;

/// <summary>Settings for <c>search_docs</c>, <c>read_doc</c> and the <c>docs://</c> resources.</summary>
public sealed class DocsOptions
{
    public const string SectionName = "EngineeringMcp:Docs";

    /// <summary>Folder that holds the documents. Nothing outside it is ever read.</summary>
    public string Root { get; set; } = string.Empty;

    /// <summary>File extensions that count as documents.</summary>
    public IList<string> Extensions { get; set; } = [".md", ".markdown"];

    /// <summary>Documents larger than this are neither indexed nor returned.</summary>
    public long MaxDocumentBytes { get; set; } = 256 * 1024;

    /// <summary>Upper bound for the number of search hits a caller can ask for.</summary>
    public int MaxSearchResults { get; set; } = 20;
}
