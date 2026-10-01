namespace EngineeringMcp.Tests.Support;

/// <summary>A uniquely named directory under the system temp folder, deleted on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "engmcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string WriteFile(string relativePath, string content)
    {
        var full = Combine(relativePath.Split('/'));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Prepare(new DirectoryInfo(Path));
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A handle still held by the OS; the temp folder is cleaned up eventually anyway.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Removes links (without touching their targets) and clears read-only flags, which git sets
    /// on object files and which stop <see cref="Directory.Delete(string, bool)"/> on Windows.
    /// </summary>
    private static void Prepare(DirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                entry.Delete();
            }
            else if (entry is DirectoryInfo child)
            {
                Prepare(child);
            }
            else
            {
                entry.Attributes = FileAttributes.Normal;
            }
        }
    }
}
