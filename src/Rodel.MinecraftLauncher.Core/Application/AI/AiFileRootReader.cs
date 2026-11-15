// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Reads an authorized directory's known fields while retaining the complete manual configuration document.</summary>
internal sealed class AiFileRootReader
{
    private readonly JsonObject? _document;
    private readonly AiApprovedPathRoot? _approved;

    private AiFileRootReader(JsonObject document) => _document = document.DeepClone().AsObject();
    private AiFileRootReader(AiApprovedPathRoot approved) => _approved = approved;

    internal string Id => _approved?.Id ?? _document!["id"]!.ToString();
    internal string Path => _approved?.Path ?? _document!["path"]!.ToString();
    internal string? Mode => _approved?.Mode ?? _document!["mode"]?.ToString();
    internal string? ApprovedPath => _approved?.PathAccess ?? _document?["pathAccess"]?.ToString();
    internal bool AllowsWrite => Mode == "read_write";
    internal bool Contains(string path) => AiPathAccess.Contains(Path, path);
    internal JsonNode? IdValue() => _document is null ? JsonValue.Create(Id) : _document["id"]?.DeepClone();
    internal JsonNode? ModeValue() => _document is null ? JsonValue.Create(Mode) : _document["mode"]?.DeepClone();
    internal JsonNode? ApprovedPathValue() => _document is null ? JsonValue.Create(ApprovedPath) : _document["pathAccess"]?.DeepClone();
    internal JsonObject Snapshot() => _document?.DeepClone().AsObject()
        ?? JsonSerializer.SerializeToNode(_approved, AiFileRootJsonContext.Default.AiApprovedPathRoot)!.AsObject();
    internal static AiFileRootReader FromDocument(JsonObject root) => new(root);
    internal static AiFileRootReader ForPath(string ancestor, string target, bool write) => new(new AiApprovedPathRoot("path", ancestor, write ? "read_write" : "read_only", target));
    internal static AiFileRootReader[] ReadConfigured(JsonObject settings)
        => [.. settings["fileRoots"]!.AsArray().OfType<JsonObject>().Select(FromDocument)];
}

/// <summary>The host-created root for one explicitly approved local path.</summary>
/// <param name="Id">The internal one-call path marker.</param>
/// <param name="Path">The actual existing ancestor pinned before access.</param>
/// <param name="Mode">The explicitly granted access mode.</param>
/// <param name="PathAccess">The exact requested target, not an unrestricted directory grant.</param>
internal sealed record AiApprovedPathRoot(string Id, string Path, string Mode, string PathAccess);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AiApprovedPathRoot))]
internal sealed partial class AiFileRootJsonContext : JsonSerializerContext;