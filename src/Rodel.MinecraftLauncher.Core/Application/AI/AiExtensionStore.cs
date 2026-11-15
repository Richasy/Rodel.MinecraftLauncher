// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Provides fixed product directories and explicitly authorized user directories.</summary>
public sealed class AiExtensionStore(LauncherPaths paths)
{
    private string FilePath => Path.Combine(paths.Root, "ai", "extensions.json");

    /// <summary>Reads registered extensions, preserving unknown and noncanonical persisted values.</summary>
    public async Task<AiExtensionReport> GetAsync(CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        return AiExtensionReport.Read(EffectiveDocument());
    }

    internal async Task<JsonObject> ReadDocumentAsync(CancellationToken ct)
    {
        using var lease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        return EffectiveDocument();
    }

    internal JsonObject EffectiveDocument()
    {
        var document = Read();
        document["mcp"] = new JsonArray();
        document["skillDirectories"] = new JsonArray();
        var roots = new JsonArray();
        if (paths.ApplicationDataDirectory is { } applicationData)
            roots.Add((JsonNode)new JsonObject { ["id"] = "builtin:app", ["path"] = applicationData, ["mode"] = "read_only" });
        roots.Add((JsonNode)new JsonObject { ["id"] = "builtin:data", ["path"] = paths.Root, ["mode"] = "read_write" });
        foreach (var root in document["fileRoots"] as JsonArray ?? [])
        {
            if (root is JsonObject item && item["id"]?.ToString().StartsWith("builtin:", StringComparison.Ordinal) == true) continue;
            roots.Add(root?.DeepClone());
        }

        document["fileRoots"] = roots;
        return document;
    }

    /// <summary>Rejects external MCP registration because launcher AI uses only packaged tools.</summary>
    public static Task<AiExtensionReport> SaveMcpAsync(AiMcpUpdate update, CancellationToken ct = default) => throw BuiltinsOnly();
    /// <summary>Registers an existing directory with its explicitly selected access mode.</summary>
    public Task<AiExtensionReport> SaveRootAsync(AiFileRootUpdate update, CancellationToken ct = default)
    {
        update = update with { };
        if (update.Mode is not ("read_only" or "read_write") || !Path.IsPathFullyQualified(update.Path) || !Directory.Exists(update.Path))
            throw new ArgumentException("Select an existing absolute directory and read_only or read_write mode.");
        return ChangeAsync(update.ExpectedRevision, state =>
        {
            var id = update.Id ?? Guid.NewGuid().ToString("N");
            CheckUserId(id);
            Upsert(state["fileRoots"]!.AsArray(), new JsonObject { ["id"] = id, ["path"] = Path.TrimEndingDirectorySeparator(Path.GetFullPath(update.Path)), ["mode"] = update.Mode });
        }, ct);
    }

    /// <summary>Rejects skill directory registration because launcher instructions are product-owned.</summary>
    public static Task<AiExtensionReport> SaveSkillDirectoryAsync(AiSkillDirectoryUpdate update, CancellationToken ct = default) => throw BuiltinsOnly();
    /// <summary>Removes one user registration only when its configuration revision still matches.</summary>
    public Task<AiExtensionReport> RemoveAsync(string collection, string id, long expectedRevision, CancellationToken ct = default)
        => ChangeAsync(expectedRevision, state =>
        {
            CheckCollection(collection);
            CheckUserId(id);
            var values = state[collection]!.AsArray();
            var old = values.FirstOrDefault(item => item?["id"]?.ToString() == id)
                ?? throw new LauncherOperationException("AI_EXTENSION_NOT_FOUND", "This user extension or directory no longer exists.");
            values.Remove(old);
        }, ct);

    /// <summary>Rejects changes to fixed, packaged MCP tools and skills.</summary>
    public static Task<AiExtensionReport> SetEnabledAsync(string collection, string id, bool enabled, long expectedRevision, CancellationToken ct = default) => throw BuiltinsOnly();

    private static LauncherOperationException BuiltinsOnly() => new("AI_BUILTIN_EXTENSIONS_ONLY", "Launcher MCP tools and skills are built in and cannot be customized.");
    private async Task<AiExtensionReport> ChangeAsync(long expectedRevision, Action<JsonObject> change, CancellationToken ct)
    {
        using var lease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        var state = Read();
        if (state["schemaVersion"]?.GetValue<int>() != 1)
            throw new LauncherOperationException("AI_SETTINGS_VERSION_UNSUPPORTED", "Extension configuration is viewable but needs a newer launcher for changes.");
        if (state["revision"]!.GetValue<long>() != expectedRevision)
            throw new LauncherOperationException("AI_CONFIGURATION_CONFLICT", "Extension configuration changed. Reload its revision before saving.");
        change(state);
        state["revision"] = expectedRevision + 1;
        await JsonFiles.WriteAsync(FilePath, state, ct).ConfigureAwait(false);
        return AiExtensionReport.Read(state);
    }

    private JsonObject Read()
    {
        try { return JsonFiles.ReadObject(FilePath); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return new JsonObject { ["schemaVersion"] = 1, ["revision"] = 0L, ["mcp"] = new JsonArray(), ["fileRoots"] = new JsonArray(), ["skillDirectories"] = new JsonArray() };
        }
    }

    private static void CheckUserId(string id)
    {
        if (id.StartsWith("builtin:", StringComparison.Ordinal)) throw new LauncherOperationException("AI_BUILTIN_ROOT_IMMUTABLE", "Built-in directory authorizations cannot be changed or removed.");
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Expected a user extension GUID. Product-owned MCP and skills cannot be overwritten.");
    }
    private static void CheckCollection(string collection)
    {
        if (collection is "mcp" or "skillDirectories") throw BuiltinsOnly();
        if (collection != "fileRoots") throw new ArgumentException("Unknown extension collection.");
    }
    private static void Upsert(JsonArray values, JsonObject item)
    {
        var old = values.FirstOrDefault(value => value?["id"]?.ToString() == item["id"]!.ToString());
        if (old is not null)
        {
            // Preserve fields written by newer compatible versions.
            foreach (var pair in old.AsObject())
                if (!item.ContainsKey(pair.Key)) item[pair.Key] = pair.Value?.DeepClone();
            values[values.IndexOf(old)] = item;
        }
        else
        {
            values.Add((JsonNode)item);
        }
    }
}