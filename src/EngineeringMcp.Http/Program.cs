using EngineeringMcp;
using EngineeringMcp.Http;
using EngineeringMcp.Http.Authentication;
using EngineeringMcp.Logging;
using EngineeringMcp.Server;
using EngineeringMcp.Sql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Serilog owns the console and the audit file. The logger belongs to this host rather than the
// static Log.Logger, and writeToProviders keeps any other logger provider registered by the
// hosting environment (or by integration tests) receiving events as well.
builder.Logging.ClearProviders();
builder.Services.AddSerilog(
    (_, logger) => logger.ConfigureEngineeringLogging(builder.Configuration, consoleToStandardError: false),
    preserveStaticLogger: true,
    writeToProviders: true);

builder.Services.AddEngineeringData(builder.Configuration);

builder.Services.AddOptions<ApiTokenOptions>()
    .Bind(builder.Configuration.GetSection(ApiTokenOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ApiTokenOptions>, ApiTokenOptionsValidator>();
builder.Services.AddSingleton<ApiTokenStore>();
builder.Services
    .AddAuthentication(ApiTokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(ApiTokenAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

builder.Services.AddOptions<OriginOptions>().Bind(builder.Configuration.GetSection(OriginOptions.SectionName));

builder.Services
    .AddMcpServer(EngineeringServer.ConfigureOptions)
    .WithHttpTransport()
    .WithEngineeringData();

var app = builder.Build();

app.Services.GetRequiredService<SqliteDemoSeeder>().EnsureCreated();

app.UseMiddleware<OriginCheckMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", server = EngineeringServer.Name, version = EngineeringServer.Version }))
    .AllowAnonymous();

app.MapMcp("/mcp").RequireAuthorization();

await app.RunAsync();

/// <summary>Entry point; public so integration tests can host it with WebApplicationFactory.</summary>
public partial class Program;
