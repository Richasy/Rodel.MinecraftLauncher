// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Content;

public sealed partial class ContentService
{
    /// <summary>Reads the primary native mod identity without assuming Fabric metadata or rejecting unknown extensions.</summary>
    internal static string? GetModIdentity(JsonNode? metadata)
    {
        if (metadata is not JsonObject value) return null;
        var identity = value["id"] ?? value["modId"] ?? (value["quilt_loader"] as JsonObject)?["id"];
        return identity is JsonValue id && id.TryGetValue<string>(out var text) ? text : null;
    }

    /// <summary>Reads every top-level native mod declaration; provided aliases and nested dependencies are not independent packages.</summary>
    internal static HashSet<string> GetModIdentities(JsonNode? metadata)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        if (GetModIdentity(metadata) is { Length: > 0 } primary) identities.Add(primary);
        if (metadata is JsonObject value && value["mods"] is JsonArray mods)
        {
            foreach (var declaration in mods)
                if (GetModIdentity(declaration) is { Length: > 0 } identity) identities.Add(identity);
        }

        return identities;
    }

    /// <summary>Matches declared identities independently of file names, versions, and package provenance.</summary>
    internal static bool ModIdentitiesOverlap(ContentEntry source, ContentEntry installed)
        => GetModIdentities(source.Metadata).Overlaps(GetModIdentities(installed.Metadata));

    /// <summary>Removes platform tracking and update policy without changing local package bytes.</summary>
    public async Task<ContentDetachResult> DetachAsync(string game, string kind, string file, string? world = null, CancellationToken ct = default)
    {
        var root = ContentRoot(game, kind, world);
        using var lease = await ResourceLease.AcquireAsync(resource: LockPath(game), cancellationToken: ct).ConfigureAwait(false);
        var contentLock = await GetLockAsync(game, ct).ConfigureAwait(false);
        var entry = (contentLock["files"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(x => x["kind"]?.GetValue<string>() == kind && (x["fileName"]?.GetValue<string>() == file || x["path"]?.GetValue<string>() == StoredPath(game, SafeChild(root, file))));
        if (entry is not null)
        {
            foreach (var key in new[]
            {
                "platform",
                "projectId",
                "versionId",
                "upstream",
                "dependencies",
                "gameVersion",
                "loader",
                "pinned",
                "channel",
                "identified",
                "identifiedAt",
                "source"
            }

            )
            {
                entry.Remove(key);
            }

            await JsonFiles.WriteAsync(LockPath(game), contentLock, ct).ConfigureAwait(false);
        }

        return new ContentDetachResult
        {
            FileName = file,
            Detached = entry is not null,
            FilesChanged = false
        };
    }

    /// <summary>Indexes readable mod declarations and reports advisory dependency findings without changing content or blocking launch.</summary>
    public async Task<ContentAnalysis> AnalyzeAsync(string game, CancellationToken ct = default)
    {
        var files = await ListAsync(game, "mods", ct: ct).ConfigureAwait(false);
        var mods = new List<ContentModDeclaration>();
        var findings = new List<ContentAnalysisFinding>();
        var incompleteNested = false;
        foreach (var file in files.Where(x => x.IsMod == true && x.Enabled))
        {
            var fileName = file.FileName;
            var metadata = file.Metadata;
            var format = file.MetadataFormat;
            if (metadata is null && file.MetadataWarning is not null)
            {
                findings.Add(new() { Kind = "unknown-metadata", FileName = fileName, Message = file.MetadataWarning });
                continue;
            }

            if (format != "mcmod.info" && metadata is not null && metadata is not JsonObject)
            {
                findings.Add(new() { Kind = "unexpected-metadata-shape", FileName = fileName });
                continue;
            }

            void Add(JsonNode? node, string idKey, string loader)
            {
                if (node is not JsonObject)
                    return;
                if (node?[idKey]?.ToString() is not { Length: > 0 } id)
                    return;
                var dependencies = new List<ContentDependencyDeclaration>();
                if (node["depends"] is JsonObject depends)
                {
                    foreach (var pair in depends)
                        dependencies.Add(new() { Id = JsonValue.Create(pair.Key)!, Range = pair.Value?.DeepClone(), Required = true });
                }

                if (node["depends"] is JsonArray quiltDepends)
                {
                    foreach (var dependency in quiltDepends.OfType<JsonObject>())
                    {
                        if (dependency["id"] is not null)
                            dependencies.Add(new() { Id = dependency["id"]!.DeepClone(), Range = dependency["versions"]?.DeepClone(), Required = dependency["optional"]?.GetValue<bool>() != true });
                    }
                }

                mods.Add(new() { Id = id, Version = node["version"]?.DeepClone(), Name = node["name"]?.DeepClone() ?? node["displayName"]?.DeepClone(), FileName = fileName, Loader = loader, Provides = node["provides"]?.DeepClone(), Depends = dependencies, Breaks = node["breaks"]?.DeepClone(), Conflicts = node["conflicts"]?.DeepClone(), NestedArchives = node["jars"]?.DeepClone() });
            }

            async Task Nested(System.IO.Compression.ZipArchive zip, JsonNode? parent, int depth)
            {
                foreach (var nested in parent?["jars"] as JsonArray ?? [])
                {
                    var nestedPath = nested?["file"]?.ToString();
                    var entry = nestedPath is null ? null : zip.GetEntry(nestedPath);
                    if (entry is null || entry.Length > 32 * 1024 * 1024 || depth >= 4)
                    {
                        incompleteNested = true;
                        findings.Add(new() { Kind = "nested-archive-not-indexed", FileName = fileName, NestedPath = nestedPath });
                        continue;
                    }

                    try
                    {
                        var input = entry.Open();
                        await using var inputDisposal = input.ConfigureAwait(false);
                        using var child = new System.IO.Compression.ZipArchive(input);
                        var descriptor = child.GetEntry("fabric.mod.json");
                        if (descriptor is null || descriptor.Length > 1024 * 1024)
                        {
                            incompleteNested = true;
                            continue;
                        }

                        var raw = descriptor.Open();
                        await using var rawDisposal = raw.ConfigureAwait(false);
                        var node = await FabricMetadataJson.ReadAsync(raw, ct).ConfigureAwait(false);
                        var before = mods.Count;
                        Add(node, "id", "fabric");
                        if (mods.Count > before)
                            mods[^1].NestedPath = nestedPath;
                        await Nested(child, node, depth + 1).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is System.IO.InvalidDataException or System.Text.Json.JsonException or IOException)
                    {
                        incompleteNested = true;
                        findings.Add(new() { Kind = "nested-metadata-warning", FileName = fileName, Message = ex.Message });
                    }
                }
            }

            if (format == "fabric.mod.json")
            {
                Add(metadata, "id", "fabric");
                if (metadata?["jars"] is JsonArray { Count: > 0 })
                {
                    using var zip = System.IO.Compression.ZipFile.OpenRead(ResolveStoredPath(game, file.Path));
                    await Nested(zip, metadata, 0).ConfigureAwait(false);
                }
            }
            else if (format == "quilt.mod.json")
            {
                Add(metadata?["quilt_loader"], "id", "quilt");
            }
            else if (format == "mcmod.info")
            {
                foreach (var node in metadata as JsonArray ?? metadata?["modList"] as JsonArray ?? [])
                    Add(node, "modid", "forge");
            }
            else if (format is "META-INF/mods.toml" or "META-INF/neoforge.mods.toml")
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(ResolveStoredPath(game, file.Path));
                using var reader = new StreamReader(archive.GetEntry(format)!.Open());
                var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                if (archive.GetEntry("META-INF/jarjar/metadata.json") is not null)
                {
                    incompleteNested = true;
                    findings.Add(new() { Kind = "jarjar-not-indexed", FileName = fileName });
                }

                string? ReadField(string body, string key) => ReadTomlField(body, key);
                var tomlDeps = ReadTomlDependencies(text);

                foreach (var table in ModTablesRegex().Matches(text))
                {
                    var body = ((System.Text.RegularExpressions.Match)table).Groups[1].Value;
                    string? Field(string key) => ReadField(body, key);
                    var owner = Field("modId");
                    if (string.IsNullOrEmpty(owner))
                        continue;
                    var declaration = new ContentModDeclaration
                    {
                        Id = owner,
                        Version = JsonValue.Create(Field("version")),
                        Name = JsonValue.Create(Field("displayName")),
                        FileName = fileName,
                        Loader = format.Contains("neoforge") ? "neoforge" : "forge",
                        Depends = tomlDeps.GetValueOrDefault(owner) ?? []
                    };
                    var incompatible = (tomlDeps.GetValueOrDefault(owner) ?? []).Where(x => x.Type is "incompatible" or "discouraged");
                    var conflicts = new JsonObject();
                    foreach (var relation in incompatible)
                        conflicts[relation.Id.ToString()] = relation.Range?.DeepClone();
                    declaration.Conflicts = conflicts;
                    mods.Add(declaration);
                }

                findings.Add(new() { FileName = fileName, Kind = "declared-ranges-not-evaluated", Message = "Mod IDs and declared dependencies indexed; version ranges are not fully evaluated." });
            }

            if (metadata is null)
                findings.Add(new() { FileName = fileName, Kind = "unknown-metadata", Message = file.MetadataWarning ?? "No recognized metadata; file remains usable." });
        }

        var indexed = mods.ToArray();
        var ids = indexed.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var mod in indexed)
        {
            foreach (var provided in mod.Provides as JsonArray ?? [])
            {
                var declaredId = provided is JsonObject declaration ? declaration["id"] : provided;
                var alias = declaredId is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
                if (!string.IsNullOrWhiteSpace(alias)) ids.Add(alias);
            }
        }

        var builtins = new HashSet<string>(["minecraft", "java", "fabricloader", "quilt_loader", "forge", "neoforge"], StringComparer.Ordinal);
        var nested = incompleteNested;
        foreach (var group in indexed.Where(x => x.NestedPath is null).GroupBy(x => x.Id).Where(x => x.Count() > 1))
            findings.Add(new() { Kind = "duplicate-id", Id = group.Key, Files = [.. group.Select(x => x.FileName)] });
        foreach (var mod in indexed)
        {
            foreach (var dependency in mod.Depends)
            {
                if (dependency.Required && dependency.Id.GetValue<string>() is { } id && !ids.Contains(id) && !builtins.Contains(id))
                    findings.Add(new() { Kind = nested ? "dependency-not-indexed" : "missing-declared-dependency", Requester = mod.Id, Dependency = dependency, FileName = mod.FileName });
            }

            foreach (var relation in new[]
            {
                "breaks",
                "conflicts"
            }

            )
            {
                foreach (var pair in (relation == "breaks" ? mod.Breaks : mod.Conflicts) as JsonObject ?? [])
                {
                    if (ids.Contains(pair.Key))
                        findings.Add(new() { Kind = "declared-" + relation, Requester = mod.Id, Target = pair.Key, Range = pair.Value?.DeepClone(), RangeEvaluated = false });
                }
            }
        }

        return new ContentAnalysis
        {
            Mods = mods,
            Findings = findings,
            Files = files
        };
    }

    private static string? ReadTomlField(string body, string key)
    {
        foreach (System.Text.RegularExpressions.Match match in TomlStringFieldsRegex().Matches(body))
        {
            if (match.Groups[1].Value != key)
                continue;
            // Literal strings keep backslashes; basic strings only terminate at an unescaped double quote.
            return match.Groups[2].Success
                ? TomlStringEscapesRegex().Replace(match.Groups[2].Value, DecodeTomlEscape)
                : match.Groups[3].Value;
        }

        return null;
    }

    private static string DecodeTomlEscape(System.Text.RegularExpressions.Match match)
    {
        var value = match.Value;
        return value[1] switch
        {
            'b' => "\b",
            't' => "\t",
            'n' => "\n",
            'f' => "\f",
            'r' => "\r",
            '"' => "\"",
            '\\' => "\\",
            _ => uint.TryParse(value.AsSpan(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var scalar)
                && scalar <= 0x10ffff && scalar is not (>= 0xd800 and <= 0xdfff)
                ? char.ConvertFromUtf32((int)scalar)
                : value
        };
    }

    [System.Text.RegularExpressions.GeneratedRegex("(?m)^[ \\t]*([A-Za-z0-9_.-]+)[ \\t]*=[ \\t]*(?:\"((?:\\\\[^\\r\\n]|[^\"\\\\\\r\\n])*)\"|'([^'\\r\\n]*)')", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex TomlStringFieldsRegex();

    [System.Text.RegularExpressions.GeneratedRegex("\\\\(?:[btnfr\"\\\\]|u[0-9A-Fa-f]{4}|U[0-9A-Fa-f]{8})", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex TomlStringEscapesRegex();

    private static Dictionary<string, List<ContentDependencyDeclaration>> ReadTomlDependencies(string text)
    {
        var result = new Dictionary<string, List<ContentDependencyDeclaration>>();
        foreach (System.Text.RegularExpressions.Match table in DependencyTablesRegex().Matches(text))
        {
            var body = table.Groups[2].Value;
            if (ReadTomlField(body, "modId") is not { } id) continue;
            var type = ReadTomlField(body, "type");
            var side = ReadTomlField(body, "side");
            var owner = table.Groups[1].Value.Trim('\"', '\'');
            if (!result.TryGetValue(owner, out var dependencies)) result[owner] = dependencies = [];
            dependencies.Add(new()
            {
                Id = JsonValue.Create(id)!,
                Range = JsonValue.Create(ReadTomlField(body, "versionRange")),
                Required = (MandatoryDependencyRegex().IsMatch(body) || type == "required") && side != "SERVER",
                Type = type,
                Side = side,
                Toml = true
            });
        }

        return result;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?ms)^[ \t]*\[\[dependencies\.([^\]]+)\]\][ \t]*(?:#[^\r\n]*)?\r?$(.*?)(?=^[ \t]*\[|\z)")]
    private static partial System.Text.RegularExpressions.Regex DependencyTablesRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"(?m)^\s*mandatory\s*=\s*true\b")]
    private static partial System.Text.RegularExpressions.Regex MandatoryDependencyRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"(?ms)^[ \t]*\[\[mods\]\][ \t]*(?:#[^\r\n]*)?\r?$(.*?)(?=^[ \t]*\[|\z)")]
    private static partial System.Text.RegularExpressions.Regex ModTablesRegex();
}