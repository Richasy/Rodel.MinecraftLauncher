// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Requests host-owned consent for an exact path and retains only explicitly selected conversation grants.</summary>
internal sealed class AiPathAccess(LauncherPaths paths)
{
    private string FileFor(string conversationId) => Path.Combine(paths.Root, "ai", "conversations",
        AiChatStore.CheckId(conversationId), "path-grants.json");

    internal static bool Contains(string parent, string target) => target.Equals(parent, StringComparison.OrdinalIgnoreCase)
        || target.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal bool Allows(string conversationId, string target, bool write)
    {
        var file = FileFor(conversationId);
        if (!File.Exists(file)) return false;
        return JsonFiles.ReadObject(file)["grants"]!.AsArray().OfType<JsonObject>().Any(grant =>
            (!write || grant["mode"]?.ToString() == "read_write")
            && (grant["scope"]?.ToString() == "all_paths" || Contains(grant["path"]!.ToString(), target)));
    }

    internal async Task GrantAsync(JsonObject binding, string scope, CancellationToken ct)
    {
        var access = binding["pathAccess"]!.AsObject();
        var file = FileFor(binding["conversationId"]!.ToString());
        using var lease = await ResourceLease.AcquireAsync(file, ct).ConfigureAwait(false);
        var document = File.Exists(file) ? JsonFiles.ReadObject(file) : new JsonObject { ["grants"] = new JsonArray() };
        var grants = document["grants"]!.AsArray();
        var path = scope == "all_paths" ? "*" : access["parentPath"]!.ToString();
        var mode = access["mode"]!.ToString();
        if (grants.OfType<JsonObject>().Any(grant => grant["scope"]?.ToString() == scope
            && grant["path"]?.ToString() == path && grant["mode"]?.ToString() == mode))
        {
            return;
        }

        grants.Add((JsonNode)new JsonObject { ["scope"] = scope, ["path"] = path, ["mode"] = mode });
        await JsonFiles.WriteAsync(file, document, ct).ConfigureAwait(false);
    }

    internal async Task AuthorizeAsync(JsonObject turn, string invocation, string name, JsonObject arguments,
        string target, bool write, Func<CancellationToken, Task> authorize,
        Func<string, string?, JsonObject, Task> emit, CancellationToken ct)
    {
        if (Allows(turn["conversationId"]!.ToString(), target, write)) return;
        var access = new AiPathAccessRequest(invocation, target,
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(target)) ?? Path.GetPathRoot(target), write ? "read_write" : "read_only");
        var review = new AiPathAccessReview(access);
        // Use a distinct binding from a possible safe-mode operation approval for the same tool invocation.
        await new AiToolApprovals(paths).WaitAsync(turn, Guid.NewGuid().ToString("N"), name, arguments, review,
            _ => Task.FromResult<AiToolReview>(review), authorize, emit, ct, pathAccess: access).ConfigureAwait(false);
    }
}