using System.IO.Pipelines;
using EngineeringMcp.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EngineeringMcp.Tests.Support;

/// <summary>
/// Runs the real server inside the test process, wired to an SDK client through a pair of
/// in-memory pipes. Everything between the client and the tools is production code: transport,
/// JSON-RPC, filters, the tool pipeline and the services behind it.
/// </summary>
public sealed class InProcessMcpServer : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly DemoDatabase _database;
    private readonly TempGitRepository _repository;

    private InProcessMcpServer(IHost host, McpClient client, CapturingLoggerProvider logs, DemoDatabase database, TempGitRepository repository)
    {
        _host = host;
        Client = client;
        Logs = logs;
        _database = database;
        _repository = repository;
    }

    public McpClient Client { get; }

    public CapturingLoggerProvider Logs { get; }

    public static async Task<InProcessMcpServer> StartAsync(IDictionary<string, string?>? overrides = null)
    {
        var database = new DemoDatabase();
        var repository = new TempGitRepository();
        try
        {
            return await StartAsync(database, repository, overrides);
        }
        catch
        {
            database.Dispose();
            repository.Dispose();
            throw;
        }
    }

    private static async Task<InProcessMcpServer> StartAsync(DemoDatabase database, TempGitRepository repository, IDictionary<string, string?>? overrides)
    {
        repository.Write("README.md", "# Service\n");
        repository.Commit("docs: start the readme");
        repository.Write("src/app.cs", "class App { }\n");
        repository.Commit("feat: add the app");

        var settings = new Dictionary<string, string?>
        {
            ["EngineeringMcp:Database:Provider"] = "Sqlite",
            ["EngineeringMcp:Database:ConnectionString"] = database.Options.ConnectionString,
            ["EngineeringMcp:Database:MaxRows"] = "100",
            ["EngineeringMcp:Docs:Root"] = Path.Combine(AppContext.BaseDirectory, "samples", "docs"),
            ["EngineeringMcp:Git:RepositoryPath"] = repository.Path,
        };

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var logs = new CapturingLoggerProvider();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.AddProvider(logs);

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        builder.Services.AddEngineeringData(builder.Configuration);
        builder.Services
            .AddMcpServer(EngineeringServer.ConfigureOptions)
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
            .WithEngineeringData();

        var host = builder.Build();
        await host.StartAsync();

        var transport = new StreamClientTransport(
            serverInput: clientToServer.Writer.AsStream(),
            serverOutput: serverToClient.Reader.AsStream());

        var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ClientInfo = new() { Name = "in-process-tests", Version = "1.0" },
        });

        return new InProcessMcpServer(host, client, logs, database, repository);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
        _database.Dispose();
        _repository.Dispose();
    }
}
