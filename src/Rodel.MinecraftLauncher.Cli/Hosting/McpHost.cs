// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli;

internal static class McpHost
{
    /// <summary>Serves the registered tools over stdio, reserving stdout for protocol messages until shutdown.</summary>
    /// <param name="application">The command execution host with its fixed launcher data root.</param>
    /// <param name="cancellationToken">Requests server shutdown without implicitly stopping independent game sessions.</param>
    /// <param name="profile">The explicit launcher, skins or compact discovery surface.</param>
    /// <param name="skinScope">Optional host-owned skin binding.</param>
    /// <returns>A task that completes when the server and its owned service provider have shut down.</returns>
    public static async Task RunAsync(LauncherApplication application, string profile, string? skinScope, CancellationToken cancellationToken)
    {
        var catalog = DiscoveryToolsCatalog.Select(profile, skinScope);
        var skins = profile == "skins";
        var dispatcher = new LauncherToolDispatcher(application, catalog, skinScope);
        var instructions = skins ? skinScope is null ? SkinToolsCatalog.Instructions : SkinToolsCatalog.ScopedInstructions : profile == "discovery" ? DiscoveryToolsCatalog.Instructions : LauncherToolsCatalog.Instructions;
        if (skinScope is not null)
        {
            var service = new Core.Skins.SkinService(application.Paths).Proposals;
            service.CheckBinding(skinScope);
            var grant = service.ReadScope(skinScope);
            instructions += $"\nThis editor is bound to scopeId={skinScope}, documentId={grant.DocumentId}. "
                + (grant.Limits.DirectEditing
                    ? "The host authorizes direct editing of this document. skin_propose immediately commits the actions and returns the accepted state and receipt; no review step is required."
                    : "Generate candidates only; the host reviews and accepts them.");
        }

        var services = new ServiceCollection();
        services.AddMcpServer(options => options.ServerInstructions = instructions)
            .WithStdioServerTransport().WithTools(catalog.Select(tool => new CatalogMcpTool(tool, dispatcher)));
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<McpServer>().RunAsync(cancellationToken);
    }

    // Explicit registration and source-generated contracts avoid reflection-based tool discovery.
    private sealed class CatalogMcpTool(LauncherToolDefinition definition, LauncherToolDispatcher dispatcher) : McpServerTool
    {
        /// <summary>Gets the catalog tool's protocol metadata, shared schemas, and operation hints.</summary>
        public override Tool ProtocolTool { get; } = new()
        {
            Name = definition.Name,
            Description = definition.Description,
            InputSchema = definition.InputSchema,
            OutputSchema = LauncherToolsCatalog.OutputSchema,
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = definition.ReadOnly,
                DestructiveHint = definition.Destructive
            },
        };
        /// <summary>Gets the empty framework metadata collection; discovery is driven by explicit catalog registration.</summary>
        public override IReadOnlyList<object> Metadata => [];

        /// <summary>Invokes the shared command dispatcher and emits matching structured and text result envelopes.</summary>
        /// <param name="request">The protocol request supplying the tool's typed JSON arguments.</param>
        /// <param name="cancellationToken">Requests cancellation of this tool invocation.</param>
        /// <returns>The actual operation envelope with a protocol error flag matching its failure or cancellation status.</returns>
        public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            var arguments = new JsonObject();
            if (request.Params?.Arguments is { } supplied)
            {
                foreach (var item in supplied)
                    arguments[item.Key] = JsonSerializer.SerializeToNode(item.Value, CliJsonContext.Default.JsonElement);
            }

            var requestId = request.Params?.Meta?["rodel.clientRequestId"]?.GetValue<string>();
            var result = await dispatcher.InvokeAsync(definition.Name, arguments, requestId, cancellationToken);
            var output = JsonSerializer.SerializeToElement(result, CliJsonContext.Default.ToolResult);
            var content = new List<ContentBlock> { new TextContentBlock { Text = output.GetRawText() } };
            if (result.Operation == "result_image_read" && result.Status == LauncherResultStatus.Succeeded
                && result.Data?.Deserialize(CliJsonContext.Default.ToolImageInfo) is { } imageInfo)
            {
                var (Bytes, MimeType) = await dispatcher.ReadImageAsync(new ResultImageReadInput(imageInfo.ResultId, imageInfo.Pointer), cancellationToken).ConfigureAwait(false);
                content.Add(ImageContentBlock.FromBytes(Bytes, MimeType));
            }

            if ((result.Operation is "skin_preview" or "skin_preview_edits" or "skin_proposal_preview") && result.Status == LauncherResultStatus.Succeeded && result.Data is JsonObject preview
                && preview["kind"]?.GetValue<string>() == "png")
            {
                content.Add(ImageContentBlock.FromBytes(
                    await File.ReadAllBytesAsync(preview["path"]!.GetValue<string>(), cancellationToken).ConfigureAwait(false), "image/png"));
            }

            return new CallToolResult
            {
                IsError = result.Status is LauncherResultStatus.Failed or LauncherResultStatus.Canceled,
                StructuredContent = output,
                Content = content,
            };
        }
    }
}