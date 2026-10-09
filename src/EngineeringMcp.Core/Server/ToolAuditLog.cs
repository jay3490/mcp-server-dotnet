using System.Text.Json;
using EngineeringMcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Server;

/// <summary>One finished tool call, as recorded in the audit log.</summary>
public sealed record ToolAuditEntry(
    string Tool,
    string Principal,
    string? SessionId,
    string? Client,
    string Outcome,
    string? ErrorCode,
    long ElapsedMilliseconds,
    IReadOnlyDictionary<string, string> Arguments);

/// <summary>
/// Writes one structured event per tool call to the <c>EngineeringMcp.Audit</c> logger category.
/// The hosts route that category to its own JSON-lines file (see <c>SerilogSetup</c>).
/// </summary>
public sealed partial class ToolAuditLog(ILoggerFactory loggerFactory, IOptions<AuditOptions> options)
{
    public const string Category = "EngineeringMcp.Audit";

    private readonly ILogger _logger = loggerFactory.CreateLogger(Category);
    private readonly int _maxArgumentLength = Math.Max(16, options.Value.MaxArgumentLength);

    public void Record(ToolAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var scope = new Dictionary<string, object?>
        {
            ["AuditEvent"] = "tool_call",
            ["SessionId"] = entry.SessionId,
            ["Client"] = entry.Client,
            ["ErrorCode"] = entry.ErrorCode,
            ["Arguments"] = entry.Arguments,
        };

        using (_logger.BeginScope(scope))
        {
            LogToolCall(entry.Tool, entry.Principal, entry.Outcome, entry.ElapsedMilliseconds);
        }
    }

    /// <summary>Raw JSON of each argument, shortened to the configured length.</summary>
    public IReadOnlyDictionary<string, string> SummarizeArguments(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return arguments.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var raw = kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? string.Empty : kv.Value.GetRawText();
                return raw.Length <= _maxArgumentLength ? raw : string.Concat(raw.AsSpan(0, _maxArgumentLength), "…");
            },
            StringComparer.Ordinal);
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Tool {Tool} called by {Principal}: {Outcome} in {ElapsedMs} ms")]
    private partial void LogToolCall(string tool, string principal, string outcome, long elapsedMs);
}
