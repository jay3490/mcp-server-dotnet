using System.Net;
using System.Net.Http.Headers;
using System.Text;
using EngineeringMcp.Server;
using ModelContextProtocol.Protocol;

namespace EngineeringMcp.Tests.Http;

public sealed class HttpTransportTests(HttpHostFactory factory) : IClassFixture<HttpHostFactory>
{
    private const string InitializeBody = """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"raw","version":"1"}}}
        """;

    private static HttpRequestMessage Initialize(string? authorization = null, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(InitializeBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        return request;
    }

    [Fact]
    public async Task Health_endpoint_needs_no_token()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/healthz", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(EngineeringServer.Name, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_token_is_challenged()
    {
        using var request = Initialize();
        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Theory]
    [InlineData("Bearer wrong-token-0123456789-abcdefghijklmnop")]
    [InlineData("Bearer ")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData(HttpHostFactory.Token)]
    [InlineData("Bearer test-token-0123456789-abcdefghijklmno")]
    public async Task Invalid_credentials_are_rejected(string authorization)
    {
        using var request = Initialize(authorization);
        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Valid_token_reaches_the_mcp_endpoint()
    {
        using var request = Initialize($"Bearer {HttpHostFactory.Token}");
        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("engineering-mcp", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bearer_scheme_is_case_insensitive()
    {
        using var request = Initialize($"bearer {HttpHostFactory.Token}");
        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_browser_origins_are_refused_even_with_a_token()
    {
        using var request = Initialize($"Bearer {HttpHostFactory.Token}", origin: "https://evil.example");
        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Configured_origins_are_allowed()
    {
        using var request = Initialize($"Bearer {HttpHostFactory.Token}", origin: HttpHostFactory.AllowedOrigin);
        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sdk_client_lists_and_calls_tools_over_streamable_http()
    {
        await using var client = await factory.CreateMcpClientAsync();

        var tools = await client.ListToolsAsync();
        var result = await client.CallToolAsync(ToolNames.SqlQuery, new Dictionary<string, object?> { ["sql"] = "SELECT count(*) AS n FROM teams" });

        Assert.Equal(ToolNames.All.Count, tools.Count);
        Assert.NotEqual(true, result.IsError);
        Assert.Contains("| 4 |", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Audit_records_the_token_name_as_the_caller()
    {
        await using var client = await factory.CreateMcpClientAsync();

        await client.CallToolAsync(ToolNames.SearchDocs, new Dictionary<string, object?> { ["query"] = "failover" });

        // Events reach test providers through Serilog, which renders string values in quotes.
        Assert.Contains(
            factory.Logs.ForCategory(ToolAuditLog.Category),
            e => e.Message.StartsWith($"Tool \"{ToolNames.SearchDocs}\" called by \"{HttpHostFactory.TokenName}\": \"ok\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sdk_client_without_a_token_cannot_connect()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => factory.CreateMcpClientAsync(token: null));
    }
}

public sealed class HttpStartupTests
{
    private sealed class TokenlessHostFactory : HttpHostFactory
    {
        protected override bool WithTokens => false;
    }

    [Fact]
    public void Server_refuses_to_start_without_tokens()
    {
        using var factory = new TokenlessHostFactory();

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("No API tokens are configured", error.ToString(), StringComparison.Ordinal);
    }
}
