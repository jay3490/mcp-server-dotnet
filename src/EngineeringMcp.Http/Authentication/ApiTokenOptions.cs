namespace EngineeringMcp.Http.Authentication;

/// <summary>Static bearer tokens accepted by the HTTP transport.</summary>
public sealed class ApiTokenOptions
{
    public const string SectionName = "EngineeringMcp:Auth";

    /// <summary>Shortest token accepted at startup, to rule out guessable values.</summary>
    public const int MinimumTokenLength = 32;

    public IList<ApiToken> Tokens { get; set; } = [];
}

public sealed class ApiToken
{
    /// <summary>Who holds the token; becomes the caller name in the audit log.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The secret itself. Supply it through an environment variable or a secret store, not appsettings.json.</summary>
    public string Token { get; set; } = string.Empty;
}
