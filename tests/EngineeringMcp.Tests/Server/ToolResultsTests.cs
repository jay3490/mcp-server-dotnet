using EngineeringMcp.Formatting;
using EngineeringMcp.Server;
using ModelContextProtocol.Protocol;

namespace EngineeringMcp.Tests.Server;

public class ToolResultsTests
{
    [Fact]
    public void Success_carries_text_and_structured_content()
    {
        var result = ToolResults.Success("hello", new { rowCount = 2 });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("hello", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(2, result.StructuredContent!.Value.GetProperty("rowCount").GetInt32());
        Assert.Null(ToolResults.ErrorCode(result));
    }

    [Fact]
    public void Failure_is_marked_and_machine_readable()
    {
        var result = ToolResults.Failure(ToolErrorCode.Forbidden, "nope");

        Assert.True(result.IsError);
        Assert.Equal("Error (forbidden): nope", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);

        var error = result.StructuredContent!.Value.GetProperty("error");
        Assert.Equal("forbidden", error.GetProperty("code").GetString());
        Assert.Equal("nope", error.GetProperty("message").GetString());
        Assert.Equal("forbidden", ToolResults.ErrorCode(result));
    }

    [Fact]
    public async Task Expected_errors_become_failure_results()
    {
        var result = await ToolResults.RunAsync(() =>
            throw new ToolErrorException(ToolErrorCode.NotFound, "missing"));

        Assert.Equal("not_found", ToolResults.ErrorCode(result));
    }

    [Fact]
    public async Task Unexpected_exceptions_are_not_swallowed()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolResults.RunAsync(() => throw new InvalidOperationException("bug")));
    }

    [Fact]
    public void Error_results_without_a_payload_report_internal_error()
    {
        var result = new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "boom" }] };

        Assert.Equal("internal_error", ToolResults.ErrorCode(result));
    }

    [Theory]
    [InlineData(ToolErrorCode.InvalidArgument, "invalid_argument")]
    [InlineData(ToolErrorCode.QueryFailed, "query_failed")]
    [InlineData(ToolErrorCode.ToolDisabled, "tool_disabled")]
    [InlineData(ToolErrorCode.Internal, "internal_error")]
    public void Error_codes_use_snake_case_on_the_wire(ToolErrorCode code, string expected)
    {
        Assert.Equal(expected, ToolErrorException.ToWireName(code));
    }

    [Fact]
    public void Structured_content_uses_camel_case_and_snake_case_enums()
    {
        var result = ToolResults.Success("x", new { RowLimit = 5, Code = ToolErrorCode.NotFound });

        var json = result.StructuredContent!.Value.GetRawText();
        Assert.Equal("""{"rowLimit":5,"code":"not_found"}""", json);
    }

    [Fact]
    public void Markdown_tables_escape_pipes_and_newlines()
    {
        var table = MarkdownTable.Render(["a", "b"], [["x|y", "line1\nline2"], [null, new string('z', 300)]]);
        var lines = table.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("| a | b |", lines[0]);
        Assert.Equal("| --- | --- |", lines[1]);
        Assert.Equal("| x\\|y | line1 line2 |", lines[2]);
        Assert.StartsWith("| NULL | zzz", lines[3], StringComparison.Ordinal);
        Assert.EndsWith("… |", lines[3], StringComparison.Ordinal);
        Assert.Equal(4, lines.Length);
    }
}
