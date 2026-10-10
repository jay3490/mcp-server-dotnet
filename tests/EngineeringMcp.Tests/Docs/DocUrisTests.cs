using EngineeringMcp.Docs;

namespace EngineeringMcp.Tests.Docs;

public class DocUrisTests
{
    [Theory]
    [InlineData("runbooks/deploying.md", "docs://runbooks/deploying.md")]
    [InlineData("guides/on call.md", "docs://guides/on%20call.md")]
    [InlineData("adr/0001-postgres.md", "docs://adr/0001-postgres.md")]
    public void Paths_round_trip_through_uris(string path, string uri)
    {
        Assert.Equal(uri, DocUris.ToUri(path));
        Assert.True(DocUris.TryGetPath(uri, out var parsed));
        Assert.Equal(path, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("docs://")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://example.com/x.md")]
    public void Rejects_other_schemes(string? uri)
    {
        Assert.False(DocUris.TryGetPath(uri, out _));
    }

    [Fact]
    public void Encoded_traversal_still_decodes_to_dot_dot_for_the_sandbox_to_reject()
    {
        Assert.True(DocUris.TryGetPath("docs://%2E%2E/secret.md", out var path));
        Assert.Equal("../secret.md", path);
    }
}
