// Copyright (c) Richasy. All rights reserved.
using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tools;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Rodel.MinecraftLauncher.Core.Application.AI;
/// <summary>Binds the current turn's authorized Core catalog without a CLI or internal MCP connection.</summary>
internal sealed class AiCoreTools(LauncherPaths paths)
{
    private readonly LauncherToolService _service = new(new LauncherClient(paths));
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    internal static readonly string[] ScopedSkinNames = ["skin_inspect", "skin_read_face", "skin_pick", "skin_select", "skin_capture", "skin_history", "skin_footprint", "skin_templates", "skin_propose", "skin_proposal_show", "skin_proposal_reject", "skin_proposal_preview", "skin_receipt"];
    /// <summary>Freezes definitions for one authorized domain; this does not grant execution permission.</summary>
    public IReadOnlyList<AIFunctionDeclaration> Load(JsonObject turn)
    {
        var skinScopeId = turn["skinScopeId"]?.GetValue<string>();
        var repairId = turn["repairId"]?.GetValue<string>();
        var operations = repairId is not null || turn["mode"]?.ToString() is "allow_operations" or "safe";
        if (skinScopeId is not null) new Skins.SkinService(paths).Proposals.CheckBinding(skinScopeId);
        var policy = new AiToolPolicy(paths, turn);
        var tools = LauncherToolService.Definitions.Where(tool => skinScopeId is not null ? ScopedSkinNames.Contains(tool.Name, StringComparer.Ordinal)
            : repairId is not null ? AiToolPolicy.RepairTools.Contains(tool.Name, StringComparer.Ordinal)
            : !tool.Name.StartsWith("skin_", StringComparison.Ordinal) && !tool.Name.StartsWith("repair_", StringComparison.Ordinal))
            .Where(tool => AiToolPolicy.CanOffer(tool.Name, operations) && policy.CanDiscover(tool.Name)).ToArray();
        _names.Clear();
        foreach (var tool in tools) _names.Add(tool.Name);
        return tools;
    }
    /// <summary>Invokes a frozen typed Core tool and attaches actual image bytes only for explicit preview/image reads.</summary>
    public async Task<JsonObject> InvokeAsync(string name, JsonObject arguments, CancellationToken ct, string? clientRequestId = null)
    {
        if (!_names.Contains(name)) throw new LauncherOperationException("AI_TOOL_NOT_OFFERED", "This tool is outside the frozen Core catalog.");
        var result = await _service.InvokeAsync(name, arguments, ct, clientRequestId).ConfigureAwait(false);
        if (result["status"]?.ToString() == "succeeded")
        {
            byte[]? bytes = null;
            var mimeType = "image/png";
            if (name == "result_image_read")
            {
                var (Bytes, MimeType) = await ToolImages.ReadAsync(paths.Root, arguments.Deserialize(ToolInputJsonContext.Default.ResultImageReadInput)!, ct).ConfigureAwait(false);
                bytes = Bytes;
                mimeType = MimeType;
            }
            else if (name is "skin_preview" or "skin_preview_edits" or "skin_proposal_preview" && result["data"]?["path"]?.GetValue<string>() is { } path)
            {
                bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            }

            if (bytes is not null) result["imageContent"] = JsonSerializer.SerializeToNode([new("image") { Data = bytes, MediaType = mimeType, ByteLength = bytes.Length }], AssistantJsonContext.Default.AiMcpContentArray);
        }

        foreach (var warning in (result["warnings"] as JsonArray ?? []).OfType<JsonObject>().Where(item => item["code"]?.ToString() == "IGNORED_ARGUMENTS"))
        {
            var query = JsonSerializer.SerializeToNode(new AiToolDiscoveryRequest(Names: [name]), AiToolDiscoveryJsonContext.Default.AiToolDiscoveryRequest)!.AsObject();
            warning["schema"] = ProtocolJson.Object(new ToolContinuation("tools_discover", query), ToolJsonContext.Default.ToolContinuation);
        }

        return result;
    }
}