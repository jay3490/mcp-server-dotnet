using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EngineeringMcp.Server;

/// <summary>
/// Wraps every <c>tools/call</c>: enforces the allow-list, applies the per-tool timeout, turns a
/// timeout into a structured result and writes an audit record whatever the outcome.
/// </summary>
public sealed partial class ToolCallPipeline(ToolPolicy policy, ToolAuditLog audit, ILogger<ToolCallPipeline> logger)
{
    public async ValueTask<CallToolResult> InvokeAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(context);

        var tool = context.Params?.Name ?? string.Empty;
        var stopwatch = Stopwatch.StartNew();
        var outcome = "exception";
        string? errorCode = null;

        try
        {
            CallToolResult result;
            if (!policy.IsAllowed(tool))
            {
                result = ToolResults.Failure(ToolErrorCode.ToolDisabled, $"Tool '{tool}' is disabled on this server.");
            }
            else
            {
                result = await InvokeWithTimeoutAsync(next, context, tool, cancellationToken).ConfigureAwait(false);
            }

            errorCode = ToolResults.ErrorCode(result);
            outcome = errorCode is null ? "ok" : "error";
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        finally
        {
            audit.Record(new ToolAuditEntry(
                Tool: tool,
                Principal: DescribePrincipal(context),
                SessionId: context.Server.SessionId,
                Client: context.Server.ClientInfo is { } client ? $"{client.Name}/{client.Version}" : null,
                Outcome: outcome,
                ErrorCode: errorCode,
                ElapsedMilliseconds: stopwatch.ElapsedMilliseconds,
                Arguments: audit.SummarizeArguments(context.Params?.Arguments)));
        }
    }

    private async ValueTask<CallToolResult> InvokeWithTimeoutAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        string tool,
        CancellationToken cancellationToken)
    {
        var timeout = policy.TimeoutFor(tool);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            return await next(context, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            LogTimedOut(tool, timeout.TotalSeconds);
            return ToolResults.Failure(
                ToolErrorCode.Timeout,
                string.Create(CultureInfo.InvariantCulture, $"Tool '{tool}' did not finish within {timeout.TotalSeconds:0.#} seconds."));
        }
    }

    private static string DescribePrincipal(RequestContext<CallToolRequestParams> context)
    {
        var identity = context.User?.Identity;
        return identity is { IsAuthenticated: true, Name: { Length: > 0 } name } ? name : "anonymous";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool {Tool} exceeded its {TimeoutSeconds} s time limit")]
    private partial void LogTimedOut(string tool, double timeoutSeconds);
}
