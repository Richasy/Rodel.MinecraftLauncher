// Copyright (c) Richasy. All rights reserved.
using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Assistant;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
namespace Rodel.MinecraftLauncher.Core.Application.AI;
/// <summary>Selects a domain or English capability keywords; empty input lists domains without loading them.</summary>
/// <param name="Group">One listed capability group.</param>
/// <param name="Query">Capability keywords, not a player-message classifier.</param>
/// <param name="Names">Optional exact names from prior discovery or history.</param>
/// <param name="Offset">Zero-based match offset.</param>
/// <param name="Limit">One to eight definitions to load.</param>
/// <param name="Reset">Whether to release previously loaded definitions before discovering a different task.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AiToolDiscoveryRequest(string? Group = null, string? Query = null, string[]? Names = null, int Offset = 0, int Limit = 6, bool Reset = false);
/// <summary>Lists a discoverable capability without its parameter schema.</summary>
/// <param name="Name">The exact catalog name.</param>
/// <param name="Description">A short capability description.</param>
internal sealed record AiDiscoveredTool(string Name, string Description);
/// <summary>Reports only matching metadata; loaded schemas are supplied through the model's tools field.</summary>
/// <param name="Groups">The available source domains.</param>
/// <param name="Tools">The selected tools, whose definitions are now active.</param>
/// <param name="Total">Number of matches in this search.</param>
/// <param name="NextOffset">Next page, when matches remain.</param>
/// <param name="UnknownNames">Requested names absent from the authorized catalog.</param>
internal sealed record AiToolDiscoveryResult(string[] Groups, AiDiscoveredTool[] Tools, int Total, int? NextOffset, string[] UnknownNames);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(AiToolDiscoveryRequest))]
[JsonSerializable(typeof(AiSuccessfulToolResult<AiToolDiscoveryResult>))]
internal partial class AiToolDiscoveryJsonContext : JsonSerializerContext;
/// <summary>Loads tools into the actual MEAI/model request only after explicit capability discovery.</summary>
internal sealed class AiToolDiscovery
{
    private readonly Dictionary<string, AIFunctionDeclaration> _available = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<CancellationToken, Task<IReadOnlyList<AIFunctionDeclaration>>>> _sources = new(StringComparer.Ordinal);
    private readonly HashSet<string> _opened = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loaded = new(StringComparer.Ordinal);
    private readonly AIFunctionDeclaration[] _bootstrap;
    /// <summary>Creates the small stable starting surface for a turn.</summary>
    public AiToolDiscovery(params AIFunctionDeclaration[] bootstrap)
    {
        _bootstrap = bootstrap;
        foreach (var tool in bootstrap) _available.Add(tool.Name, tool);
    }
    /// <summary>Gets only the definitions actually entering the next model call.</summary>
    public IReadOnlyList<AIFunctionDeclaration> Visible => [.. _bootstrap, .. _loaded.Order(StringComparer.Ordinal).Select(name => _available[name])];
    /// <summary>Gets the authorized catalog already discovered by the host, independently of model visibility.</summary>
    public IReadOnlyList<AIFunctionDeclaration> Available => [.. _available.Values];
    /// <summary>Gets the compact names of authorized capability domains.</summary>
    public string[] Groups => [.. _sources.Keys.Order(StringComparer.Ordinal)];
    /// <summary>Adds host-owned metadata without putting its schemas into the model request.</summary>
    public void Add(string group, IEnumerable<AIFunctionDeclaration> tools)
    {
        foreach (var tool in tools)
        {
            if (!_available.TryAdd(tool.Name, tool)) throw new InvalidOperationException("Duplicate tool identity: " + tool.Name);
            _groups.Add(tool.Name, group);
        }

        _sources.TryAdd(group, _ => Task.FromResult<IReadOnlyList<AIFunctionDeclaration>>([]));
        _opened.Add(group);
    }
    /// <summary>Registers an external domain whose connection and catalog are opened only when requested.</summary>
    public void Defer(string group, Func<CancellationToken, Task<IReadOnlyList<AIFunctionDeclaration>>> load) => _sources.Add(group, load);
    private async Task OpenAsync(string group, CancellationToken ct)
    {
        if (!_sources.TryGetValue(group, out var source)) throw new ArgumentException("Unknown capability group. Use tools_discover with no arguments to list available groups.");
        if (_opened.Contains(group)) return;
        var tools = await source(ct).ConfigureAwait(false);
        Add(group, tools);
    }
    /// <summary>Resolves a qualified tool remembered from history without opening unrelated external sources.</summary>
    public async Task ResolveAsync(string name, CancellationToken ct)
    {
        if (_available.ContainsKey(name) || !name.StartsWith("mcp_", StringComparison.Ordinal)) return;
        var group = name.StartsWith("mcp_rodel_files_", StringComparison.Ordinal) ? "files"
            : name.StartsWith("mcp_rodel_web_", StringComparison.Ordinal) ? "web"
            : name.StartsWith("mcp_rodel_search_", StringComparison.Ordinal) ? "search" : "external";
        if (_sources.ContainsKey(group)) await OpenAsync(group, ct).ConfigureAwait(false);
    }
    /// <summary>Searches or browses one scoped group and activates only the selected page of exact definitions.</summary>
    public async Task<AiToolDiscoveryResult> DiscoverAsync(AiToolDiscoveryRequest request, CancellationToken ct)
    {
        if (request.Offset < 0 || request.Limit is < 1 or > 8 || request.Names?.Any(string.IsNullOrWhiteSpace) == true) throw new ArgumentException("Use a nonnegative offset, 1..8 limit and exact nonempty names.");
        if (request.Reset) _loaded.Clear();
        if (request.Group is not null) await OpenAsync(request.Group, ct).ConfigureAwait(false);
        foreach (var name in request.Names ?? []) await ResolveAsync(name, ct).ConfigureAwait(false);
        if (request.Group is null && string.IsNullOrWhiteSpace(request.Query) && request.Names is not { Length: > 0 }) return new(Groups, [], 0, null, []);
        var terms = request.Query?.Split([' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var matches = _available.Values.Where(tool => !_bootstrap.Any(seed => seed.Name == tool.Name)
            && (request.Group is null || _groups.GetValueOrDefault(tool.Name) == request.Group)
            && (request.Names is not { Length: > 0 } || request.Names.Contains(tool.Name, StringComparer.Ordinal)))
            .Select(tool => (Tool: tool, Score: terms.Sum(term => tool.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ? 4 : tool.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) == true ? 1 : 0)))
            .Where(item => terms.Length == 0 || item.Score > 0).OrderByDescending(item => item.Score).ThenBy(item => item.Tool.Name, StringComparer.Ordinal).ToArray();
        var page = matches.Skip(request.Offset).Take(request.Limit).ToArray();
        foreach (var item in page) _loaded.Add(item.Tool.Name);
        return new(Groups, [.. page.Select(item => new AiDiscoveredTool(item.Tool.Name, item.Tool.Description is { Length: > 240 } text ? text[..240] : item.Tool.Description ?? string.Empty))], matches.Length,
            request.Offset + page.Length < matches.Length ? request.Offset + page.Length : null, [.. (request.Names ?? []).Where(name => !_available.ContainsKey(name))]);
    }
    /// <summary>Provides the small discovery schema, independent of all managed or external MCP schemas.</summary>
    public static AIFunctionDeclaration Tool() => AiToolDeclarations.Create("tools_discover",
        "Discover and load tools only when the task needs software, game, skin, file or web operations. Empty input lists groups. Select a group, English capability keywords or exact names; browse remaining matches with nextOffset. Loaded definitions appear on the next model call. reset releases prior task definitions. Discovery grants no permission.",
        AiToolDiscoveryJsonContext.Default.AiToolDiscoveryRequest.GetJsonSchemaAsNode(new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true }).AsObject());
}