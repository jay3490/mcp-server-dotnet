namespace EngineeringMcp.Http;

public sealed class OriginOptions
{
    public const string SectionName = "EngineeringMcp:Http";

    /// <summary>
    /// Browser origins allowed to call the MCP endpoint, e.g. <c>https://tools.example.com</c>.
    /// Requests without an Origin header (non-browser clients) are not affected.
    /// </summary>
    public IList<string> AllowedOrigins { get; set; } = [];
}

/// <summary>
/// Rejects browser requests from origins that are not configured. This is the DNS-rebinding
/// protection the MCP transport specification asks servers to apply to the Origin header.
/// </summary>
public sealed class OriginCheckMiddleware(RequestDelegate next, Microsoft.Extensions.Options.IOptions<OriginOptions> options)
{
    private readonly HashSet<string> _allowed = new(
        options.Value.AllowedOrigins.Select(o => o.TrimEnd('/')),
        StringComparer.OrdinalIgnoreCase);

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && !_allowed.Contains(origin.TrimEnd('/')))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return context.Response.WriteAsJsonAsync(new { error = "origin_not_allowed", origin });
        }

        return next(context);
    }
}
