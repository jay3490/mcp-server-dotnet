using System.Globalization;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace EngineeringMcp.Logging;

public static class SerilogSetup
{
    private const string ConsoleTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Applies the shared logging setup: levels from the <c>Serilog</c> configuration section and a
    /// console sink.
    /// </summary>
    /// <param name="consoleToStandardError">
    /// Must be <see langword="true"/> for the stdio transport, where stdout carries protocol
    /// messages and a single stray log line would corrupt the stream.
    /// </param>
    public static LoggerConfiguration ConfigureEngineeringLogging(
        this LoggerConfiguration logger,
        IConfiguration configuration,
        bool consoleToStandardError)
    {
        ArgumentNullException.ThrowIfNull(logger);

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
