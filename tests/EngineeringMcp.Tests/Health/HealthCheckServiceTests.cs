using System.Net;
using EngineeringMcp.Health;
using EngineeringMcp.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EngineeringMcp.Tests.Health;

public sealed class HealthCheckServiceTests : IDisposable
{
    private readonly StubHttpHandler _handler = new StubHttpHandler()
        .Returns("https://billing.internal/healthz", HttpStatusCode.OK)
        .Returns("https://search.internal/healthz", HttpStatusCode.ServiceUnavailable)
        .Returns("https://moved.internal/healthz", HttpStatusCode.Found, new Uri("https://elsewhere.example/"))
        .Returns("https://legacy.internal/status", HttpStatusCode.NoContent)
        .Hangs("https://slow.internal/healthz")
        .Fails("https://down.internal/healthz", "Connection refused");

    private readonly ServiceProvider _services;

    public HealthCheckServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EngineeringMcp:HealthChecks:TimeoutSeconds"] = "1",
                ["EngineeringMcp:HealthChecks:Endpoints:0:Name"] = "billing",
                ["EngineeringMcp:HealthChecks:Endpoints:0:Url"] = "https://billing.internal/healthz",
                ["EngineeringMcp:HealthChecks:Endpoints:1:Name"] = "search",
                ["EngineeringMcp:HealthChecks:Endpoints:1:Url"] = "https://search.internal/healthz",
                ["EngineeringMcp:HealthChecks:Endpoints:2:Name"] = "moved",
                ["EngineeringMcp:HealthChecks:Endpoints:2:Url"] = "https://moved.internal/healthz",
                ["EngineeringMcp:HealthChecks:Endpoints:3:Name"] = "legacy",
                ["EngineeringMcp:HealthChecks:Endpoints:3:Url"] = "https://legacy.internal/status",
                ["EngineeringMcp:HealthChecks:Endpoints:3:ExpectedStatus"] = "204",
                ["EngineeringMcp:HealthChecks:Endpoints:4:Name"] = "slow",
                ["EngineeringMcp:HealthChecks:Endpoints:4:Url"] = "https://slow.internal/healthz",
                ["EngineeringMcp:HealthChecks:Endpoints:5:Name"] = "down",
                ["EngineeringMcp:HealthChecks:Endpoints:5:Url"] = "https://down.internal/healthz",
                ["EngineeringMcp:HealthChecks:Endpoints:6:Name"] = "ftp",
                ["EngineeringMcp:HealthChecks:Endpoints:6:Url"] = "ftp://files.internal/",
            })
            .Build();

        var services = new ServiceCollection().AddEngineeringData(configuration);
        services.AddHttpClient(HealthCheckService.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _handler);
        _services = services.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    private HealthCheckService Service => _services.GetRequiredService<HealthCheckService>();

    [Fact]
    public async Task Healthy_endpoint_reports_status_and_latency()
    {
        var status = Assert.Single(await Service.CheckAsync("billing", CancellationToken.None));

        Assert.True(status.Healthy);
        Assert.Equal(200, status.StatusCode);
        Assert.Null(status.Error);
        Assert.True(status.LatencyMilliseconds >= 0);
    }

    [Fact]
    public async Task Endpoint_names_are_case_insensitive()
    {
        var status = Assert.Single(await Service.CheckAsync("BILLING", CancellationToken.None));

        Assert.Equal("billing", status.Name);
    }

    [Fact]
    public async Task An_exact_allowed_url_is_accepted()
    {
        var status = Assert.Single(await Service.CheckAsync("https://search.internal/healthz", CancellationToken.None));

        Assert.Equal("search", status.Name);
        Assert.False(status.Healthy);
        Assert.Equal(503, status.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("https://billing.internal/admin")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("payments")]
    public async Task Anything_off_the_allow_list_is_refused_without_a_request(string endpoint)
    {
        var error = await Assert.ThrowsAsync<ToolErrorException>(() => Service.CheckAsync(endpoint, CancellationToken.None));

        Assert.Equal(ToolErrorCode.Forbidden, error.Code);
        Assert.Contains("billing", error.Message, StringComparison.Ordinal);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Redirects_are_reported_not_followed()
    {
        var status = Assert.Single(await Service.CheckAsync("moved", CancellationToken.None));

        Assert.Equal(302, status.StatusCode);
        Assert.False(status.Healthy);
        Assert.DoesNotContain(_handler.Requests, u => u.Host == "elsewhere.example");
    }

    [Fact]
    public async Task Expected_status_overrides_the_2xx_rule()
    {
        var status = Assert.Single(await Service.CheckAsync("legacy", CancellationToken.None));

        Assert.True(status.Healthy);
        Assert.Equal(204, status.StatusCode);
    }

    [Fact]
    public async Task Slow_endpoints_time_out()
    {
        var status = Assert.Single(await Service.CheckAsync("slow", CancellationToken.None));

        Assert.False(status.Healthy);
        Assert.Null(status.StatusCode);
        Assert.Contains("No response", status.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connection_failures_are_reported_per_endpoint()
    {
        var status = Assert.Single(await Service.CheckAsync("down", CancellationToken.None));

        Assert.False(status.Healthy);
        Assert.Equal("Connection refused", status.Error);
    }

    [Fact]
    public async Task Non_http_urls_in_config_are_never_requested()
    {
        var status = Assert.Single(await Service.CheckAsync("ftp", CancellationToken.None));

        Assert.False(status.Healthy);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Without_a_name_every_endpoint_is_checked()
    {
        var statuses = await Service.CheckAsync(null, CancellationToken.None);

        Assert.Equal(["billing", "search", "moved", "legacy", "slow", "down", "ftp"], statuses.Select(s => s.Name));
    }

    [Fact]
    public async Task No_configured_endpoints_is_unavailable()
    {
        using var services = new ServiceCollection()
            .AddEngineeringData(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        var error = await Assert.ThrowsAsync<ToolErrorException>(() =>
            services.GetRequiredService<HealthCheckService>().CheckAsync(null, CancellationToken.None));

        Assert.Equal(ToolErrorCode.Unavailable, error.Code);
    }
}
