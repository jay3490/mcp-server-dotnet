using System.Text.Json;
using EngineeringMcp.Server;
using EngineeringMcp.Tests.Support;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EngineeringMcp.Tests.Server;

public sealed class McpRoundTripTests : IAsyncLifetime
{
    private InProcessMcpServer _server = null!;

    private McpClient Client => _server.Client;

    public async Task InitializeAsync() => _server = await InProcessMcpServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    private static string Text(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static JsonElement Structured(CallToolResult result) =>
        result.StructuredContent ?? throw new Xunit.Sdk.XunitException("No structured content.");

    [Fact]
    public void Handshake_reports_server_identity_and_instructions()
    {
        Assert.NotNull(Client.ServerInfo);
        Assert.Equal(EngineeringServer.Name, Client.ServerInfo.Name);
        Assert.Equal(EngineeringServer.Version, Client.ServerInfo.Version);
        Assert.Contains("sql_schema", Client.ServerInstructions ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lists_every_tool_as_read_only()
    {
        var tools = await Client.ListToolsAsync();

        Assert.Equal(ToolNames.All.Order(), tools.Select(t => t.Name).Order());
        Assert.All(tools, t => Assert.True(t.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.All(tools, t => Assert.False(t.ProtocolTool.Annotations?.DestructiveHint));
        Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
    }

    [Fact]
    public async Task Sql_query_returns_a_table_and_structured_rows()
    {
        var result = await Client.CallToolAsync(ToolNames.SqlQuery, new Dictionary<string, object?>
        {
            ["sql"] = "SELECT name, tier FROM services WHERE tier = 1 ORDER BY name",
            ["maxRows"] = 2,
        });

        Assert.NotEqual(true, result.IsError);
        Assert.StartsWith("| name | tier |", Text(result), StringComparison.Ordinal);
        Assert.Contains("2-row limit", Text(result), StringComparison.Ordinal);

        var structured = Structured(result);
        Assert.Equal(2, structured.GetProperty("rowCount").GetInt32());
        Assert.True(structured.GetProperty("truncated").GetBoolean());
        Assert.Equal("api-gateway", structured.GetProperty("rows")[0][0].GetString());
    }

    [Fact]
    public async Task Rejected_sql_comes_back_as_a_structured_error()
    {
        var result = await Client.CallToolAsync(ToolNames.SqlQuery, new Dictionary<string, object?> { ["sql"] = "DELETE FROM teams" });

        Assert.True(result.IsError);
        Assert.Equal("invalid_argument", Structured(result).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Sql_schema_describes_tables()
    {
        var result = await Client.CallToolAsync(ToolNames.SqlSchema, new Dictionary<string, object?> { ["table"] = "incidents" });

        Assert.Contains("### incidents (table)", Text(result), StringComparison.Ordinal);
        Assert.Equal("sqlite", Structured(result).GetProperty("dialect").GetString());
    }

    [Fact]
    public async Task Search_then_read_a_document()
    {
        var search = await Client.CallToolAsync(ToolNames.SearchDocs, new Dictionary<string, object?> { ["query"] = "rollback deployment", ["limit"] = 1 });
        var path = Structured(search).GetProperty("results")[0].GetProperty("path").GetString();

        var read = await Client.CallToolAsync(ToolNames.ReadDoc, new Dictionary<string, object?> { ["path"] = path });

        Assert.Equal("runbooks/deploying.md", path);
        Assert.StartsWith("# Runbook: deploying a service", Text(read), StringComparison.Ordinal);
        Assert.Equal("docs://runbooks/deploying.md", Structured(read).GetProperty("uri").GetString());
    }

    [Theory]
    [InlineData("../appsettings.json")]
    [InlineData("runbooks/../../secret.md")]
    [InlineData("/etc/passwd")]
    public async Task Read_doc_refuses_paths_outside_the_root(string path)
    {
        var result = await Client.CallToolAsync(ToolNames.ReadDoc, new Dictionary<string, object?> { ["path"] = path });

        Assert.True(result.IsError);
        Assert.Equal("forbidden", Structured(result).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Git_tools_read_the_configured_repository()
    {
        var log = await Client.CallToolAsync(ToolNames.GitLog, new Dictionary<string, object?> { ["maxCount"] = 5 });
        var diff = await Client.CallToolAsync(ToolNames.GitDiffSummary, null);

        Assert.Equal(2, Structured(log).GetProperty("commits").GetArrayLength());
        Assert.Contains("feat: add the app", Text(log), StringComparison.Ordinal);
        Assert.Equal(1, Structured(diff).GetProperty("filesChanged").GetInt32());
        Assert.Contains("| added | src/app.cs | 1 | 0 |", Text(diff), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_status_without_endpoints_is_unavailable()
    {
        var result = await Client.CallToolAsync(ToolNames.HttpStatus, null);

        Assert.Equal("unavailable", Structured(result).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Documents_are_listed_and_readable_as_resources()
    {
        var resources = await Client.ListResourcesAsync();
        var templates = await Client.ListResourceTemplatesAsync();
        var read = await Client.ReadResourceAsync("docs://runbooks/incident-response.md");

        Assert.Contains(resources, r => r.Uri == "docs://architecture.md" && r.Title == "Platform architecture overview");
        Assert.Equal("docs://{+path}", Assert.Single(templates).UriTemplate);
        var contents = Assert.IsType<TextResourceContents>(Assert.Single(read.Contents));
        Assert.Equal("text/markdown", contents.MimeType);
        Assert.StartsWith("# Runbook: incident response", contents.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resource_reads_go_through_the_sandbox()
    {
        var error = await Assert.ThrowsAsync<McpProtocolException>(() => Client.ReadResourceAsync("docs://%2E%2E/appsettings.json").AsTask());

        Assert.Contains("..", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prompts_embed_live_data()
    {
        var prompts = await Client.ListPromptsAsync();
        var schema = await Client.GetPromptAsync(EngineeringPrompts.ExplainSchemaName, new Dictionary<string, object?> { ["table"] = "deployments" });
        var changes = await Client.GetPromptAsync(EngineeringPrompts.SummariseRecentChangesName, new Dictionary<string, object?> { ["count"] = 5 });

        Assert.Equal([EngineeringPrompts.ExplainSchemaName, EngineeringPrompts.SummariseRecentChangesName], prompts.Select(p => p.Name).Order());

        var schemaText = Assert.IsType<TextContentBlock>(Assert.Single(schema.Messages).Content).Text;
        Assert.Contains("- duration_seconds INTEGER, not null", schemaText, StringComparison.Ordinal);

        var changesText = Assert.IsType<TextContentBlock>(Assert.Single(changes.Messages).Content).Text;
        Assert.Contains("feat: add the app", changesText, StringComparison.Ordinal);
        Assert.Contains("| added | src/app.cs |", changesText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_tool_call_is_audited()
    {
        await Client.CallToolAsync(ToolNames.SqlQuery, new Dictionary<string, object?> { ["sql"] = "SELECT 1 AS one" });
        await Client.CallToolAsync(ToolNames.SqlQuery, new Dictionary<string, object?> { ["sql"] = "DROP TABLE teams" });

        var entries = _server.Logs.ForCategory(ToolAuditLog.Category).ToList();

        Assert.Equal(2, entries.Count);
        Assert.Equal("ok", entries[0].Properties["Outcome"]);
        Assert.Equal("error", entries[1].Properties["Outcome"]);
        Assert.Equal("invalid_argument", entries[1].Properties["ErrorCode"]);
        Assert.Equal("in-process-tests/1.0", entries[1].Properties["Client"]);
        var arguments = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(entries[1].Properties["Arguments"]);
        Assert.Equal("DROP TABLE teams", arguments["sql"]);
    }
}

public sealed class McpPolicyTests
{
    [Fact]
    public async Task Allow_list_hides_and_blocks_other_tools()
    {
        await using var server = await InProcessMcpServer.StartAsync(new Dictionary<string, string?>
        {
            ["EngineeringMcp:Tools:Allowed:0"] = ToolNames.SqlSchema,
            ["EngineeringMcp:Tools:Allowed:1"] = ToolNames.SearchDocs,
        });

        var tools = await server.Client.ListToolsAsync();
        var blocked = await server.Client.CallToolAsync(ToolNames.SqlQuery, new Dictionary<string, object?> { ["sql"] = "SELECT 1" });

        Assert.Equal([ToolNames.SearchDocs, ToolNames.SqlSchema], tools.Select(t => t.Name).Order());
        Assert.True(blocked.IsError);
        Assert.Equal("tool_disabled", blocked.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());

        var audit = Assert.Single(server.Logs.ForCategory(ToolAuditLog.Category));
        Assert.Equal("tool_disabled", audit.Properties["ErrorCode"]);
    }

    [Fact]
    public async Task Per_tool_timeout_stops_a_long_call()
    {
        await using var server = await InProcessMcpServer.StartAsync(new Dictionary<string, string?>
        {
            // The database would allow 60 s; the tool policy allows 1 s and must win.
            ["EngineeringMcp:Database:CommandTimeoutSeconds"] = "60",
            ["EngineeringMcp:Tools:Timeouts:sql_query"] = "1",
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await server.Client.CallToolAsync(ToolNames.SqlQuery, new Dictionary<string, object?>
        {
            ["sql"] = "WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c) SELECT count(*) FROM c",
        });

        Assert.True(result.IsError);
        Assert.Equal("timeout", result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Unknown_tool_names_in_the_allow_list_fail_startup()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => InProcessMcpServer.StartAsync(new Dictionary<string, string?>
        {
            ["EngineeringMcp:Tools:Allowed:0"] = "sql_querry",
        }));

        Assert.Contains("sql_querry", error.Message, StringComparison.Ordinal);
    }
}
