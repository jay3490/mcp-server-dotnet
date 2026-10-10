using EngineeringMcp.Docs;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EngineeringMcp.Server;

/// <summary>
/// Publishes every document in the docs folder as an MCP resource (<c>docs://path</c>). The list
/// is computed per request, so new files appear without a restart; reads go through the same
/// sandbox as <c>read_doc</c>.
/// </summary>
public static class DocResources
{
    public const string MimeType = "text/markdown";

    public static IMcpServerBuilder WithDocResources(this IMcpServerBuilder builder)
    {
        return builder
            .WithListResourcesHandler((context, cancellationToken) =>
                ValueTask.FromResult(List(Store(context.Services ?? context.Server.Services))))
            .WithListResourceTemplatesHandler((_, _) => ValueTask.FromResult(new ListResourceTemplatesResult
            {
                ResourceTemplates =
                [
                    new ResourceTemplate
                    {
                        UriTemplate = DocUris.Template,
                        Name = "doc",
                        Title = "Engineering document",
                        Description = "A markdown document from the docs folder, addressed by its relative path.",
                        MimeType = MimeType,
                    },
                ],
            }))
            .WithReadResourceHandler(async (context, cancellationToken) =>
                await ReadAsync(Store(context.Services ?? context.Server.Services), context.Params?.Uri, cancellationToken).ConfigureAwait(false));
    }

    internal static ListResourcesResult List(DocumentStore store)
    {
        var resources = store.List()
            .Select(file => new Resource
            {
                Uri = DocUris.ToUri(file.RelativePath),
                Name = file.RelativePath,
                Title = DocumentStore.ExtractTitle(File.ReadAllText(file.FullPath), file.RelativePath),
                MimeType = MimeType,
                Size = file.Length,
            })
            .ToList();

        return new ListResourcesResult { Resources = resources };
    }

    internal static async Task<ReadResourceResult> ReadAsync(DocumentStore store, string? uri, CancellationToken cancellationToken)
    {
        if (!DocUris.TryGetPath(uri, out var path))
        {
            throw new McpProtocolException($"Unknown resource '{uri}'. Document URIs look like {DocUris.Scheme}runbooks/deploying.md.", McpErrorCode.ResourceNotFound);
        }

        DocContent doc;
        try
        {
            doc = await store.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (ToolErrorException ex)
        {
            var code = ex.Code == ToolErrorCode.NotFound ? McpErrorCode.ResourceNotFound : McpErrorCode.InvalidParams;
            throw new McpProtocolException(ex.Message, ex, code);
        }

        return new ReadResourceResult
        {
            Contents = [new TextResourceContents { Uri = DocUris.ToUri(doc.Path), MimeType = MimeType, Text = doc.Text }],
        };
    }

    private static DocumentStore Store(IServiceProvider? services) =>
        (services ?? throw new InvalidOperationException("The MCP request has no service provider."))
            .GetRequiredService<DocumentStore>();
}
