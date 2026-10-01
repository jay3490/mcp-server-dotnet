namespace EngineeringMcp.Docs;

/// <summary>A document's text plus the metadata the tools report.</summary>
public sealed record DocContent(string Path, string Title, string Text, long Length, DateTime LastWriteTimeUtc);

/// <summary>Reads documents through the <see cref="DocsSandbox"/>.</summary>
public sealed class DocumentStore(DocsSandbox sandbox)
{
    public DocsSandbox Sandbox => sandbox;

    public async Task<DocContent> ReadAsync(string? relativePath, CancellationToken cancellationToken)
    {
        var file = sandbox.Resolve(relativePath);
        var text = await File.ReadAllTextAsync(file.FullPath, cancellationToken).ConfigureAwait(false);
        return new DocContent(file.RelativePath, ExtractTitle(text, file.RelativePath), text, file.Length, file.LastWriteTimeUtc);
    }

    public IReadOnlyList<DocFile> List() => sandbox.EnumerateDocuments();

    /// <summary>The first level-one heading, or the file name without extension.</summary>
    public static string ExtractTitle(string markdown, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        using var reader = new StringReader(markdown);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                return trimmed[2..].Trim();
            }
        }

        return Path.GetFileNameWithoutExtension(relativePath);
    }
}
