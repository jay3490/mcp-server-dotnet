namespace EngineeringMcp.Docs;

/// <summary>
/// Maps document paths to MCP resource URIs of the form <c>docs://runbooks/deploying.md</c> and back.
/// Each path segment is percent-encoded, so names with spaces survive the round trip.
/// </summary>
public static class DocUris
{
    public const string Scheme = "docs://";

    public const string Template = "docs://{+path}";

    public static string ToUri(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return Scheme + string.Join('/', relativePath.Split('/').Select(Uri.EscapeDataString));
    }

    /// <summary>Extracts the relative path; the sandbox still validates it before any file access.</summary>
    public static bool TryGetPath(string? uri, out string relativePath)
    {
        relativePath = string.Empty;
        if (uri is null || !uri.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase) || uri.Length == Scheme.Length)
        {
            return false;
        }

        relativePath = string.Join('/', uri[Scheme.Length..].Split('/').Select(Uri.UnescapeDataString));
        return true;
    }
}
