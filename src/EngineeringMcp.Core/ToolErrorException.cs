namespace EngineeringMcp;

/// <summary>Machine-readable categories for failed tool calls.</summary>
public enum ToolErrorCode
{
    InvalidArgument,
    NotFound,
    Forbidden,
    Timeout,
    QueryFailed,
    Unavailable,
    ToolDisabled,
    Internal,
}

/// <summary>
/// An expected failure that should reach the client as a structured error result rather than as
/// a protocol error. Services throw it; the tool pipeline turns it into a tool result.
/// </summary>
public sealed class ToolErrorException : Exception
{
    public ToolErrorException(ToolErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ToolErrorException(ToolErrorCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public ToolErrorCode Code { get; }

    /// <summary>The snake_case form used on the wire, e.g. <c>invalid_argument</c>.</summary>
    public static string ToWireName(ToolErrorCode code) => code switch
    {
        ToolErrorCode.InvalidArgument => "invalid_argument",
        ToolErrorCode.NotFound => "not_found",
        ToolErrorCode.Forbidden => "forbidden",
        ToolErrorCode.Timeout => "timeout",
        ToolErrorCode.QueryFailed => "query_failed",
        ToolErrorCode.Unavailable => "unavailable",
        ToolErrorCode.ToolDisabled => "tool_disabled",
        _ => "internal_error",
    };
}
