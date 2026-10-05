using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace EngineeringMcp.Server;

/// <summary>
/// Builds tool results. Every tool answers with readable text for the model plus the same data as
/// structured JSON; failures use the same shape with <c>isError</c> set, so a client never has to
/// parse an exception message to learn what went wrong.
/// </summary>
public static class ToolResults
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static CallToolResult Success(string text, object structured)
    {
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(structured, JsonOptions),
        };
    }

    public static CallToolResult Failure(ToolErrorCode code, string message)
    {
        var wireCode = ToolErrorException.ToWireName(code);
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = $"Error ({wireCode}): {message}" }],
            StructuredContent = JsonSerializer.SerializeToElement(new { error = new { code = wireCode, message } }, JsonOptions),
        };
    }

    /// <summary>Runs a tool body and turns an expected <see cref="ToolErrorException"/> into a failure result.</summary>
    public static async Task<CallToolResult> RunAsync(Func<Task<CallToolResult>> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (ToolErrorException ex)
        {
            return Failure(ex.Code, ex.Message);
        }
    }

    /// <inheritdoc cref="RunAsync(Func{Task{CallToolResult}})"/>
    public static CallToolResult Run(Func<CallToolResult> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            return body();
        }
        catch (ToolErrorException ex)
        {
            return Failure(ex.Code, ex.Message);
        }
    }

    /// <summary>Reads the error code back out of a failure result, e.g. for audit logging.</summary>
    public static string? ErrorCode(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsError != true)
        {
            return null;
        }

        return result.StructuredContent is { ValueKind: JsonValueKind.Object } content
            && content.TryGetProperty("error", out var error)
            && error.TryGetProperty("code", out var code)
                ? code.GetString()
                : ToolErrorException.ToWireName(ToolErrorCode.Internal);
    }
}
