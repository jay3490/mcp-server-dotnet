using EngineeringMcp.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace EngineeringMcp.Tests.Http;

/// <summary>Hosts the HTTP server in memory with a demo database and a known token.</summary>
public class HttpHostFactory : WebApplicationFactory<Program>
{
    public const string TokenName = "ci-pipeline";
    public const string Token = "test-token-0123456789-abcdefghijklmnop";
    public const string AllowedOrigin = "https://tools.example.com";

    private readonly DemoDatabase _database = new();

    public CapturingLoggerProvider Logs { get; } = new();

    protected virtual bool WithTokens => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("EngineeringMcp:Database:ConnectionString", _database.Options.ConnectionString);
        builder.UseSetting("EngineeringMcp:Database:SeedScript", "");
        builder.UseSetting("EngineeringMcp:Docs:Root", Path.Combine(AppContext.BaseDirectory, "samples", "docs"));
        builder.UseSetting("EngineeringMcp:Git:RepositoryPath", "");
        builder.UseSetting("EngineeringMcp:Audit:Path", "");
        builder.UseSetting("EngineeringMcp:Http:AllowedOrigins:0", AllowedOrigin);

        if (WithTokens)
        {
            builder.UseSetting("EngineeringMcp:Auth:Tokens:0:Name", TokenName);
            builder.UseSetting("EngineeringMcp:Auth:Tokens:0:Token", Token);
        }

        builder.ConfigureServices(services => services.AddLogging(logging => logging.AddProvider(Logs)));
    }

    /// <summary>An SDK client speaking Streamable HTTP to the in-memory server.</summary>
    public Task<McpClient> CreateMcpClientAsync(string? token = Token)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(Server.BaseAddress, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = token is null ? null : new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        };

        var transport = new HttpClientTransport(options, CreateClient(), loggerFactory: null, ownsHttpClient: true);
        return McpClient.CreateAsync(transport);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _database.Dispose();
        }
    }
}
