using EngineeringMcp.Configuration;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Docs;

/// <summary>A document inside the docs root.</summary>
/// <param name="RelativePath">Path below the root with forward slashes, e.g. <c>runbooks/deploying.md</c>.</param>
public sealed record DocFile(string RelativePath, string FullPath, long Length, DateTime LastWriteTimeUtc);

/// <summary>
/// Maps caller-supplied relative paths onto files below the docs root and refuses anything that
/// could reach outside it.
/// </summary>
/// <remarks>
/// Checks, in order: control characters, absolute or drive-qualified paths, <c>..</c> segments,
/// containment of the normalised full path, extension, existence, symbolic links or junctions on
/// any segment below the root, and size. Links are refused rather than resolved because a link
/// that points inside the root today can be re-pointed outside it tomorrow.
/// </remarks>
public sealed class DocsSandbox
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly DocsOptions _options;
    private readonly HashSet<string> _extensions;

    public DocsSandbox(IOptions<DocsOptions> options)
    {
        _options = options.Value;
        _extensions = new HashSet<string>(_options.Extensions, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(_options.Root))
        {
            RootPath = string.Empty;
            return;
        }

        var root = new DirectoryInfo(Path.GetFullPath(_options.Root));

        // The operator chose the root, so a link at the root itself is trusted and resolved once.
        var target = root.Exists ? root.ResolveLinkTarget(returnFinalTarget: true) : null;
        RootPath = Path.TrimEndingDirectorySeparator(target?.FullName ?? root.FullName);
    }

    /// <summary>Absolute path of the docs root, or empty when docs are not configured.</summary>
    public string RootPath { get; }

    public bool IsConfigured => RootPath.Length > 0 && Directory.Exists(RootPath);

    public DocFile Resolve(string? relativePath)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ToolErrorException(ToolErrorCode.InvalidArgument, "A document path is required.");
        }

        if (relativePath.Any(char.IsControl))
        {
            throw new ToolErrorException(ToolErrorCode.InvalidArgument, "The path contains control characters.");
        }

        var normalized = relativePath.Trim().Replace('\\', '/');

        if (normalized.StartsWith('/') || normalized.Contains(':', StringComparison.Ordinal) || Path.IsPathRooted(normalized))
        {
            throw new ToolErrorException(ToolErrorCode.Forbidden, "Absolute paths are not allowed; use a path relative to the docs root.");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s != ".")
            .ToArray();

        if (segments.Any(s => s == ".."))
        {
            throw new ToolErrorException(ToolErrorCode.Forbidden, "Paths may not contain '..' segments.");
        }

        if (segments.Any(s => s.EndsWith('.') || s.EndsWith(' ')))
        {
            // Windows silently strips these, so "guide.md." would alias "guide.md".
            throw new ToolErrorException(ToolErrorCode.Forbidden, "Path segments may not end with a dot or a space.");
        }

        if (segments.Length == 0)
        {
            throw new ToolErrorException(ToolErrorCode.InvalidArgument, "A document path is required.");
        }

        var fullPath = Path.GetFullPath(Path.Combine([RootPath, .. segments]));
        if (!fullPath.StartsWith(RootPath + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new ToolErrorException(ToolErrorCode.Forbidden, "The path resolves outside the docs root.");
        }

        if (!_extensions.Contains(Path.GetExtension(fullPath)))
        {
            throw new ToolErrorException(
                ToolErrorCode.Forbidden,
                $"Only documents with these extensions can be read: {string.Join(", ", _extensions)}.");
        }

        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new ToolErrorException(ToolErrorCode.NotFound, $"No document at '{string.Join('/', segments)}'.");
        }

        EnsureNoLinks(file);

        if (file.Length > _options.MaxDocumentBytes)
        {
            throw new ToolErrorException(
                ToolErrorCode.InvalidArgument,
                $"The document is {file.Length} bytes; the limit is {_options.MaxDocumentBytes}.");
        }

        return ToDocFile(file);
    }

    /// <summary>
    /// Every readable document below the root. Linked files and directories are skipped, as are
    /// files over the size limit.
    /// </summary>
    public IReadOnlyList<DocFile> EnumerateDocuments()
    {
        if (!IsConfigured)
        {
            return [];
        }

        var results = new List<DocFile>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(RootPath));

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (IsLink(entry))
                {
                    continue;
                }

                if (entry is DirectoryInfo child)
                {
                    pending.Push(child);
                }
                else if (entry is FileInfo file
                    && _extensions.Contains(file.Extension)
                    && file.Length <= _options.MaxDocumentBytes)
                {
                    results.Add(ToDocFile(file));
                }
            }
        }

        results.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));
        return results;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new ToolErrorException(ToolErrorCode.Unavailable, "No docs folder is configured on this server.");
        }
    }

    private void EnsureNoLinks(FileInfo file)
    {
        FileSystemInfo? current = file;
        while (current is not null && current.FullName.Length > RootPath.Length)
        {
            if (IsLink(current))
            {
                throw new ToolErrorException(ToolErrorCode.Forbidden, "Symbolic links and junctions are not followed.");
            }

            current = current is FileInfo f ? f.Directory : ((DirectoryInfo)current).Parent;
        }
    }

    private static bool IsLink(FileSystemInfo entry) =>
        entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

    private DocFile ToDocFile(FileInfo file)
    {
        var relative = Path.GetRelativePath(RootPath, file.FullName).Replace(Path.DirectorySeparatorChar, '/');
        return new DocFile(relative, file.FullName, file.Length, file.LastWriteTimeUtc);
    }
}
