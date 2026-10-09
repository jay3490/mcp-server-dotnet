using System.Globalization;
using EngineeringMcp.Configuration;
using EngineeringMcp.Server;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Formatting.Compact;

namespace EngineeringMcp.Logging;

public static class SerilogSetup
{
    private const string ConsoleTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Applies the shared logging setup: levels from the <c>Serilog</c> configuration section, a
    /// console sink, and a separate JSON-lines file that receives only the tool-call audit trail.
    /// </summary>
    /// <param name="consoleToStandardError">
    /// Must be <see langword="true"/> for the stdio transport, where stdout carries protocol
    /// messages and a single stray log line would corrupt the stream.
    /// </param>
    /// <param name="baseDirectory">Directory a relative audit path is resolved against.</param>
    public static LoggerConfiguration ConfigureEngineeringLogging(
        this LoggerConfiguration logger,
        IConfiguration configuration,
        bool consoleToStandardError,
        string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var audit = configuration.GetSection(AuditOptions.SectionName).Get<AuditOptions>() ?? new AuditOptions();
        if (!string.IsNullOrWhiteSpace(audit.Path))
        {
            var path = Path.GetFullPath(audit.Path, baseDirectory ?? AppContext.BaseDirectory);
            logger = logger.WriteTo.Logger(sub => sub
                .Filter.ByIncludingOnly(Matching.FromSource(ToolAuditLog.Category))
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    path,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: Math.Max(1, audit.RetainedFileCount)));
        }

        return logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: ConsoleTemplate,
                formatProvider: CultureInfo.InvariantCulture,
                standardErrorFromLevel: consoleToStandardError ? LogEventLevel.Verbose : null);
    }
}
