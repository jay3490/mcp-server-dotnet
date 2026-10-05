using Microsoft.Data.Sqlite;

namespace EngineeringMcp.Configuration;

/// <summary>
/// Makes configured paths absolute against one base directory, so the server finds its data no
/// matter which working directory an MCP client launches it from.
/// </summary>
internal static class PathResolution
{
    public static string Resolve(string path, string baseDirectory) =>
        string.IsNullOrWhiteSpace(path) ? path : Path.GetFullPath(path, baseDirectory);

    public static string ResolveSqliteConnectionString(string connectionString, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);
        var source = builder.DataSource;
        if (string.IsNullOrWhiteSpace(source)
            || source == ":memory:"
            || source.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(source))
        {
            return connectionString;
        }

        builder.DataSource = Path.GetFullPath(source, baseDirectory);
        return builder.ToString();
    }
}
