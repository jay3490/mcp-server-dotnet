using System.Diagnostics;
using EngineeringMcp.Configuration;
using Microsoft.Extensions.Options;

namespace EngineeringMcp.Health;

public sealed record EndpointStatus(string Name, string Url, bool Healthy, int? StatusCode, long LatencyMilliseconds, string? Error);

/// <summary>
/// Requests health endpoints from a fixed allow-list. Callers pick an endpoint by name (or by its
/// exact configured URL); there is no way to make the server fetch any other address.
/// </summary>
/// <remarks>
/// Redirects are not followed, so an allowed endpoint cannot bounce the request somewhere else,
/// and response bodies are never read or returned.
/// </remarks>
public sealed class HealthCheckService(IHttpClientFactory httpClientFactory, IOptions<HealthCheckOptions> options)
{
    public const string HttpClientName = "engineering-mcp-health";

    private readonly HealthCheckOptions _options = options.Value;

    public IReadOnlyList<HealthEndpoint> Endpoints => _options.Endpoints.ToList();

    public async Task<IReadOnlyList<EndpointStatus>> CheckAsync(string? endpoint, CancellationToken cancellationToken)
    {
        if (_options.Endpoints.Count == 0)
        {
            throw new ToolErrorException(ToolErrorCode.Unavailable, "No health endpoints are configured on this server.");
        }

        var targets = string.IsNullOrWhiteSpace(endpoint)
            ? _options.Endpoints.ToList()
            : [Find(endpoint.Trim())];

        return await Task.WhenAll(targets.Select(t => CheckOneAsync(t, cancellationToken))).ConfigureAwait(false);
    }

    private HealthEndpoint Find(string endpoint)
    {
        var byName = _options.Endpoints.FirstOrDefault(e => string.Equals(e.Name, endpoint, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName;
        }

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var requested))
        {
            var byUrl = _options.Endpoints.FirstOrDefault(e =>
                Uri.TryCreate(e.Url, UriKind.Absolute, out var allowed) && allowed == requested);
            if (byUrl is not null)
            {
                return byUrl;
            }
        }

        var known = string.Join(", ", _options.Endpoints.Select(e => e.Name));
        throw new ToolErrorException(ToolErrorCode.Forbidden, $"'{endpoint}' is not on the allow-list. Known endpoints: {known}.");
    }

    private async Task<EndpointStatus> CheckOneAsync(HealthEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(endpoint.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new EndpointStatus(endpoint.Name, endpoint.Url, false, null, 0, "Configured URL is not an absolute http(s) URL.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));

        var client = httpClientFactory.CreateClient(HttpClientName);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            var status = (int)response.StatusCode;
            var healthy = endpoint.ExpectedStatus is { } expected ? status == expected : response.IsSuccessStatusCode;
            return new EndpointStatus(endpoint.Name, endpoint.Url, healthy, status, stopwatch.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new EndpointStatus(endpoint.Name, endpoint.Url, false, null, stopwatch.ElapsedMilliseconds, $"No response within {_options.TimeoutSeconds} s.");
        }
        catch (HttpRequestException ex)
        {
            return new EndpointStatus(endpoint.Name, endpoint.Url, false, null, stopwatch.ElapsedMilliseconds, ex.Message);
        }
    }
}
