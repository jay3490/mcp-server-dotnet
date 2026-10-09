using System.Text.Json;
using EngineeringMcp.Configuration;
using EngineeringMcp.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Tests.Server;

public class ToolPolicyTests
{
    private static ToolPolicy Policy(ToolPolicyOptions options) => new(Options.Create(options));

    [Fact]
    public void Every_tool_is_allowed_without_an_allow_list()
    {
        var policy = Policy(new ToolPolicyOptions());

        Assert.All(ToolNames.All, name => Assert.True(policy.IsAllowed(name)));
        Assert.Equal(ToolNames.All, policy.AllowedTools);
    }

    [Fact]
    public void An_allow_list_exposes_only_the_listed_tools()
    {
        var policy = Policy(new ToolPolicyOptions { Allowed = [ToolNames.SqlSchema, ToolNames.SearchDocs] });

        Assert.True(policy.IsAllowed(ToolNames.SqlSchema));
        Assert.False(policy.IsAllowed(ToolNames.SqlQuery));
        Assert.False(policy.IsAllowed("anything_else"));
        Assert.Equal([ToolNames.SqlSchema, ToolNames.SearchDocs], policy.AllowedTools);
    }

    [Fact]
    public void An_empty_allow_list_disables_everything()
    {
        var policy = Policy(new ToolPolicyOptions { Allowed = [] });

        Assert.Empty(policy.AllowedTools);
    }

    [Fact]
    public void Timeouts_fall_back_to_the_default()
    {
        var policy = Policy(new ToolPolicyOptions
        {
            DefaultTimeoutSeconds = 30,
            Timeouts = new Dictionary<string, int> { [ToolNames.SqlQuery] = 5, [ToolNames.HttpStatus] = 0 },
        });

        Assert.Equal(TimeSpan.FromSeconds(5), policy.TimeoutFor(ToolNames.SqlQuery));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.TimeoutFor(ToolNames.GitLog));
        Assert.Equal(TimeSpan.FromSeconds(1), policy.TimeoutFor(ToolNames.HttpStatus));
    }

    [Fact]
    public void Validator_rejects_unknown_tool_names()
    {
        var result = new ToolPolicyOptionsValidator().Validate(null, new ToolPolicyOptions
        {
            Allowed = [ToolNames.SqlQuery, "sql_querry"],
            Timeouts = new Dictionary<string, int> { ["git_blame"] = 3 },
        });

        Assert.True(result.Failed);
        Assert.Contains("sql_querry", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("git_blame", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_accepts_known_tool_names()
    {
        var result = new ToolPolicyOptionsValidator().Validate(null, new ToolPolicyOptions { Allowed = [.. ToolNames.All] });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Audit_arguments_are_shortened()
    {
        var audit = new ToolAuditLog(NullLoggerFactory.Instance, Options.Create(new AuditOptions { MaxArgumentLength = 20 }));
        using var document = JsonDocument.Parse("""{ "sql": "SELECT * FROM deployments WHERE id > 100", "maxRows": 5, "flag": null }""");
        var arguments = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

        var summary = audit.SummarizeArguments(arguments);

        Assert.Equal("SELECT * FROM deploy…", summary["sql"]);
        Assert.Equal("5", summary["maxRows"]);
        Assert.Equal("null", summary["flag"]);
        Assert.Empty(audit.SummarizeArguments(null));
    }
}
