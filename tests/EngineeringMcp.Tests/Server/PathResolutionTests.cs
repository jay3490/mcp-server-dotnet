using EngineeringMcp.Configuration;

namespace EngineeringMcp.Tests.Server;

public class PathResolutionTests
{
    private static readonly string Base = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "app"));

    [Fact]
    public void Relative_paths_resolve_against_the_base_directory()
    {
        Assert.Equal(Path.Combine(Base, "samples", "docs"), PathResolution.Resolve("samples/docs", Base));
    }

    [Fact]
    public void Absolute_and_empty_paths_are_left_alone()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "elsewhere");

        Assert.Equal(absolute, PathResolution.Resolve(absolute, Base));
        Assert.Equal("", PathResolution.Resolve("", Base));
    }

    [Fact]
    public void Sqlite_data_source_is_made_absolute()
    {
        var resolved = PathResolution.ResolveSqliteConnectionString("Data Source=samples/data/demo.db;Cache=Shared", Base);

        Assert.Contains(Path.Combine(Base, "samples", "data", "demo.db"), resolved, StringComparison.Ordinal);
        Assert.Contains("Cache=Shared", resolved, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=file:demo.db?mode=ro")]
    [InlineData("")]
    public void Special_sqlite_sources_are_untouched(string connectionString)
    {
        Assert.Equal(connectionString, PathResolution.ResolveSqliteConnectionString(connectionString, Base));
    }
}
