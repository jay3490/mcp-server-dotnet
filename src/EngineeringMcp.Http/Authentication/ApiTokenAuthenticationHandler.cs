using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Http.Authentication;

/// <summary>
/// Holds SHA-256 digests of the configured tokens. Comparing fixed-length digests with
/// <see cref="CryptographicOperations.FixedTimeEquals"/> keeps the check constant-time regardless
/// of how long the presented token is.
/// </summary>
public sealed class ApiTokenStore
{
    private readonly IReadOnlyList<(string Name, byte[] Digest)> _tokens;

    public ApiTokenStore(IOptions<ApiTokenOptions> options)
    {
        _tokens = options.Value.Tokens
            .Select(t => (t.Name, Digest: Hash(t.Token)))
            .ToList();
    }

    public string? FindName(string presented)
    {
        var digest = Hash(presented);
        string? match = null;

        // Check every entry so the time taken does not reveal which token matched.
        foreach (var (name, expected) in _tokens)
        {
            if (CryptographicOperations.FixedTimeEquals(digest, expected))
            {
                match = name;
            }
        }

        return match;
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}

/// <summary>Authenticates <c>Authorization: Bearer &lt;token&gt;</c> against <see cref="ApiTokenStore"/>.</summary>
public sealed class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiTokenStore tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiToken";

    private const string BearerPrefix = "Bearer ";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("Only bearer tokens are accepted."));
        }

        var presented = header[BearerPrefix.Length..].Trim();
        if (presented.Length == 0 || tokens.FindName(presented) is not { } name)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid token."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.NameIdentifier, name)], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"engineering-mcp\"";
        return Task.CompletedTask;
    }
}

/// <summary>Refuses to start with no tokens, unnamed tokens, duplicate names or short tokens.</summary>
public sealed class ApiTokenOptionsValidator : IValidateOptions<ApiTokenOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiTokenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.Tokens.Count == 0)
        {
            failures.Add($"No API tokens are configured. Set {ApiTokenOptions.SectionName}:Tokens:0:Name and :Token (for example through environment variables).");
        }

        foreach (var (token, index) in options.Tokens.Select((t, i) => (t, i)))
        {
            if (string.IsNullOrWhiteSpace(token.Name))
            {
                failures.Add($"Token {index} has no name.");
            }

            if (token.Token.Length < ApiTokenOptions.MinimumTokenLength)
            {
                failures.Add($"Token '{token.Name}' is shorter than {ApiTokenOptions.MinimumTokenLength} characters.");
            }
        }

        var duplicates = options.Tokens.GroupBy(t => t.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            failures.Add($"Token names must be unique: {string.Join(", ", duplicates)}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
