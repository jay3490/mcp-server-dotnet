using System.Text;

namespace EngineeringMcp.Formatting;

/// <summary>Renders rows as a GitHub-flavoured markdown table.</summary>
public static class MarkdownTable
{
    public const int MaxCellLength = 200;

    public static string Render(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        AppendRow(builder, headers);
        builder.Append('|');
        foreach (var _ in headers)
        {
            builder.Append(" --- |");
        }

        builder.Append('\n');
        foreach (var row in rows)
        {
            AppendRow(builder, row);
        }

        return builder.ToString();
    }

    /// <summary>Escapes pipes, flattens line breaks and shortens long values; null becomes <c>NULL</c>.</summary>
    public static string Cell(string? value)
    {
        if (value is null)
        {
            return "NULL";
        }

        var flat = value.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ');
        if (flat.Length > MaxCellLength)
        {
            flat = string.Concat(flat.AsSpan(0, MaxCellLength), "…");
        }

        return flat.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string?> cells)
    {
        builder.Append('|');
        foreach (var cell in cells)
        {
            builder.Append(' ').Append(Cell(cell)).Append(" |");
        }

        builder.Append('\n');
    }
}
