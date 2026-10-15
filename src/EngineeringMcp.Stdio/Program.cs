using EngineeringMcp;
using EngineeringMcp.Logging;
using EngineeringMcp.Server;
using EngineeringMcp.Sql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

// Settings and relative data paths resolve against the executable's folder, not the working
// directory, because MCP clients launch stdio servers from wherever they happen to be.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// stdout carries the JSON-RPC stream, so the default console logger has to go and every log line
// is sent to stderr instead.
builder.Logging.ClearProviders();
builder.Services.AddSerilog(
    (_, logger) => logger.ConfigureEngineeringLogging(builder.Configuration, consoleToStandardError: true),
    preserveStaticLogger: true);

builder.Services.AddEngineeringData(builder.Configuration);
builder.Services
    .AddMcpServer(EngineeringServer.ConfigureOptions)
    .WithStdioServerTransport()
    .WithEngineeringData();

using var host = builder.Build();
host.Services.GetRequiredService<SqliteDemoSeeder>().EnsureCreated();
await host.RunAsync();
