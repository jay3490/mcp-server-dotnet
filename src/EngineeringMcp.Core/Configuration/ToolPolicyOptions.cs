namespace EngineeringMcp.Configuration;

/// <summary>Which tools are exposed and how long each may run.</summary>
public sealed class ToolPolicyOptions
{
    public const string SectionName = "EngineeringMcp:Tools";

    /// <summary>
    /// Tool names to expose. When unset, every tool is exposed. Unknown names fail startup so a
    /// typo cannot silently disable the allow-list.
    /// </summary>
    public IList<string>? Allowed { get; set; }

    /// <summary>Time limit for a tool call that has no entry in <see cref="Timeouts"/>.</summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>Per-tool time limits in seconds, keyed by tool name.</summary>
    public IDictionary<string, int> Timeouts { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
}

/// <summary>Where the audit trail of tool calls is written.</summary>
public sealed class AuditOptions
{
    public const string SectionName = "EngineeringMcp:Audit";

    /// <summary>Rolling JSON-lines file; the date is inserted before the extension. Empty disables the file.</summary>
    public string Path { get; set; } = "logs/audit-.jsonl";

    public int RetainedFileCount { get; set; } = 14;

    /// <summary>Argument values longer than this are cut short in the audit record.</summary>
    public int MaxArgumentLength { get; set; } = 1000;
}
