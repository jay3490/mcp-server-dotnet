using System.Collections.Concurrent;
using System.Net;

namespace EngineeringMcp.Tests.Support;

/// <summary>Answers requests from a table of canned responses and records what was asked.</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<CancellationToken, Task<HttpResponseMessage>>> _routes = new(StringComparer.Ordinal);

    public ConcurrentQueue<Uri> Requests { get; } = new();

    public StubHttpHandler Returns(string url, HttpStatusCode status, Uri? location = null)
    {
        _routes[url] = _ =>
        {
            var response = new HttpResponseMessage(status);
            if (location is not null)
            {
                response.Headers.Location = location;
            }

            return Task.FromResult(response);
        };
        return this;
    }

    public StubHttpHandler Hangs(string url)
    {
        _routes[url] = async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        return this;
    }

    public StubHttpHandler Fails(string url, string message)
    {
        _routes[url] = _ => throw new HttpRequestException(message);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request.RequestUri!);
        return _routes.TryGetValue(request.RequestUri!.ToString(), out var route)
            ? route(cancellationToken)
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
