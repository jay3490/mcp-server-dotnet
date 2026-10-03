namespace EngineeringMcp.Configuration;

/// <summary>Settings for <c>git_log</c> and <c>git_diff_summary</c>.</summary>
public sealed class GitOptions
{
    public const string SectionName = "EngineeringMcp:Git";

    /// <summary>A working tree or bare repository. Empty disables the git tools.</summary>
    public string RepositoryPath { get; set; } = string.Empty;

    /// <summary>Most commits one <c>git_log</c> call can return.</summary>
    public int MaxCommits { get; set; } = 50;

    /// <summary>Most files listed by one <c>git_diff_summary</c> call; totals still cover every file.</summary>
    public int MaxDiffFiles { get; set; } = 200;
}
