using EngineeringMcp.Configuration;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Server;

/// <summary>Answers "may this tool run, and for how long" from <see cref="ToolPolicyOptions"/>.</summary>
public sealed class ToolPolicy
{
    private readonly HashSet<string>? _allowed;
    private readonly Dictionary<string, TimeSpan> _timeouts;
    private readonly TimeSpan _defaultTimeout;

    public ToolPolicy(IOptions<ToolPolicyOptions> options)
    {
        var settings = options.Value;

        if (settings.Allowed is { } allowed)
        {
            _allowed = new HashSet<string>(allowed, StringComparer.Ordinal);
        }

        _defaultTimeout = TimeSpan.FromSeconds(Math.Max(1, settings.DefaultTimeoutSeconds));
        _timeouts = settings.Timeouts.ToDictionary(
            kv => kv.Key,
            kv => TimeSpan.FromSeconds(Math.Max(1, kv.Value)),
            StringComparer.Ordinal);
    }

    public bool IsAllowed(string toolName) => _allowed is null || _allowed.Contains(toolName);

    public IReadOnlyList<string> AllowedTools => ToolNames.All.Where(IsAllowed).ToList();

    public TimeSpan TimeoutFor(string toolName) => _timeouts.GetValueOrDefault(toolName, _defaultTimeout);
}

/// <summary>Fails startup when the allow-list or timeouts name a tool that does not exist.</summary>
public sealed class ToolPolicyOptionsValidator : IValidateOptions<ToolPolicyOptions>
{
    public ValidateOptionsResult Validate(string? name, ToolPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var unknown = (options.Allowed ?? [])
            .Concat(options.Timeouts.Keys)
            .Where(n => !ToolNames.All.Contains(n, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return unknown.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{ToolPolicyOptions.SectionName} names unknown tool(s): {string.Join(", ", unknown)}. " +
                $"Known tools: {string.Join(", ", ToolNames.All)}.");
    }
}
