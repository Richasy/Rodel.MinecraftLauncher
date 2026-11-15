// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Packs;

public sealed partial class PackService
{
    /// <summary>Publishes verified publisher references and authorized local packages; the multiplayer profile limits files to enabled content and shared configuration, excluding personal settings and local binaries.</summary>
    public async Task<PackExportResult> ExportAsync(string instanceId, string destination, string format = "mrpack", bool includeLocalMods = false, bool forMultiplayer = false, CancellationToken cancellationToken = default)
    {
        if (format is not ("mrpack" or "curseforge"))
            throw new ArgumentException("Export format must be mrpack or curseforge.");
        if (forMultiplayer) includeLocalMods = false;
        var instance = new InstanceStore(_paths).Get(instanceId);
        using var gameLease = await ResourceLease.AcquireAsync(resource: instance.GameDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var contentLease = await ResourceLease.AcquireAsync(resource: ContentLock(instance), cancellationToken: cancellationToken).ConfigureAwait(false);
        var packLock = ReadLock(instance);
        var owned = Records(packLock["files"] as JsonArray ?? []);
        var content = File.Exists(ContentLock(instance)) ? JsonFiles.ReadObject(ContentLock(instance))["files"] as JsonArray ?? [] : [];
        foreach (var item in content)
        {
            var path = item?["path"]?.GetValue<string>();
            if (path is not null && !Path.IsPathRooted(path))
                owned.TryAdd(path, item!.Deserialize(PackJsonContext.Default.PackFileRecord)!);
        }

        var dependencies = await ExportDependenciesAsync(instance, packLock, cancellationToken).ConfigureAwait(false);
        var manifest = (packLock["format"]?.GetValue<string>() == format && packLock["manifest"] is JsonObject original) ? (JsonObject)original.DeepClone() : [];
        manifest["name"] = instance.Name;
        var references = new JsonArray();
        var manualReferences = new JsonArray();
        var excluded = new List<PackExcludedFile>();
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cursePairs = new HashSet<string>();
        foreach (var pair in owned)
        {
            var record = pair.Value;
            var relative = pair.Key;
            if (forMultiplayer && !MultiplayerFile(relative)) continue;
            var file = PackFiles.SafeRead(instance.GameDirectory, relative);
            var current = await PackFiles.Sha1Async(file, cancellationToken).ConfigureAwait(false);
            if (current is null || current != record.Sha1)
                continue;
            var platform = record.Platform;
            var url = record.DownloadUrl;
            if (format == "mrpack" && platform == "modrinth" && !string.IsNullOrWhiteSpace(url))
            {
                var declaration = record.Metadata is JsonObject metadata && metadata["path"] is not null ? (JsonObject)metadata.DeepClone() : [];
                declaration["path"] = relative;
                declaration["hashes"] = new JsonObject
                {
                    ["sha1"] = current,
                    ["sha512"] = await PackFiles.Sha512Async(file, cancellationToken).ConfigureAwait(false)
                };
                declaration["fileSize"] = new FileInfo(file).Length;
                if (declaration["downloads"] is not JsonArray)
                    declaration["downloads"] = new JsonArray(JsonValue.Create(url));
                references.Add((JsonNode)declaration);
                referenced.Add(relative);
            }
            else if (format == "curseforge" && platform == "curseforge" && record.IdentityVerified is not false && long.TryParse(record.ProjectId, out var project) && long.TryParse(record.VersionId, out var version))
            {
                if (cursePairs.Add($"{project}:{version}"))
                    references.Add((JsonNode)new JsonObject { ["projectID"] = project, ["fileID"] = version, ["required"] = true });
                referenced.Add(relative);
            }
        }

        if (packLock["format"]?.GetValue<string>() == format && packLock["missing"] is JsonArray missing)
        {
            foreach (var item in missing)
            {
                if (item?["metadata"] is not JsonObject metadata)
                    continue;
                if (format == "mrpack")
                {
                    var relative = metadata["path"]!.GetValue<string>();
                    if (forMultiplayer && !MultiplayerFile(relative)) continue;
                    if (referenced.Add(relative))
                        references.Add(metadata.DeepClone());
                }
                else if (metadata["path"] is not null)
                {
                    var relative = metadata["path"]!.GetValue<string>();
                    if (forMultiplayer && !MultiplayerFile(relative)) continue;
                    if (referenced.Add(relative)) manualReferences.Add(metadata.DeepClone());
                }
                else
                {
                    var pair = $"{metadata["projectID"]}:{metadata["fileID"]}";
                    if (cursePairs.Add(pair))
                        references.Add(metadata.DeepClone());
                }
            }
        }

        if (format == "mrpack")
        {
            manifest["formatVersion"] = 1;
            manifest["game"] = "minecraft";
            manifest["versionId"] ??= Guid.NewGuid().ToString("N");
            manifest["dependencies"] = dependencies.DeepClone();
        }
        else
        {
            manifest["manifestType"] = "minecraftModpack";
            manifest["manifestVersion"] = 1;
            manifest["version"] ??= "1.0.0";
            manifest["author"] ??= string.Empty;
            manifest["overrides"] = "overrides";
            var loaders = new JsonArray();
            foreach (var dependency in dependencies.Where(entry => entry.Key != "minecraft"))
            {
                loaders.Add((JsonNode)new JsonObject
                {
                    ["id"] = (dependency.Key switch
                    {
                        "fabric-loader" => "fabric",
                        "quilt-loader" => "quilt",
                        var other => other
                    }) + "-" + dependency.Value?.GetValue<string>(),
                    ["primary"] = loaders.Count == 0
                });
            }

            manifest["minecraft"] = new JsonObject
            {
                ["version"] = dependencies["minecraft"]?.DeepClone(),
                ["modLoaders"] = loaders
            };
        }

        if (forMultiplayer)
        {
            // Preserve the requirement without redistributing bytes or inventing a download source.
            // Empty download lists enter the ordinary hash-checked manual completion workflow.
            foreach (var file in PackFiles.Enumerate(instance.GameDirectory, relative => ExportDirectory(relative, owned.Keys), allowReadLinks: true))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(instance.GameDirectory, file).Replace('\\', '/');
                if (!MultiplayerFile(relative) || referenced.Contains(relative)) continue;
                var first = relative.Split('/')[0];
                if (first is not ("mods" or "resourcepacks" or "shaderpacks") && !relative.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) continue;
                var declaration = new JsonObject
                {
                    ["path"] = relative,
                    ["hashes"] = new JsonObject
                    {
                        ["sha1"] = await PackFiles.Sha1Async(file, cancellationToken).ConfigureAwait(false),
                        ["sha512"] = await PackFiles.Sha512Async(file, cancellationToken).ConfigureAwait(false)
                    },
                    ["fileSize"] = new FileInfo(file).Length,
                    ["downloads"] = new JsonArray()
                };
                if (owned.TryGetValue(relative, out var source))
                    declaration["rodelSource"] = new JsonObject { ["platform"] = source.Platform, ["projectId"] = source.ProjectId, ["versionId"] = source.VersionId };
                (format == "mrpack" ? references : manualReferences).Add((JsonNode)declaration);
                referenced.Add(relative);
            }

            foreach (var declaration in references.Concat(manualReferences).OfType<JsonObject>().Where(item => item["path"] is not null && (item["downloads"] is not JsonArray urls || urls.Count == 0)))
                excluded.Add(new(declaration["path"]!.GetValue<string>(), "Declared with verification hashes for manual completion; no redistributable download source."));
        }

        if (format == "curseforge")
        {
            manifest.Remove("rodelMissingFiles");
            if (manualReferences.Count > 0) manifest["rodelMissingFiles"] = manualReferences;
        }

        manifest["files"] = references;
        destination = Path.GetFullPath(destination);
        PackFiles.NoLinks(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var destinationLease = await ResourceLease.AcquireAsync(resource: destination, cancellationToken: cancellationToken).ConfigureAwait(false);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var overrides = 0;
        try
        {
            {
                var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, true);
                await using (output.ConfigureAwait(false))
                {
                    using var zip = new ZipArchive(output, ZipArchiveMode.Create, true);
                    await WriteJsonEntryAsync(zip, format == "mrpack" ? "modrinth.index.json" : "manifest.json", manifest, cancellationToken).ConfigureAwait(false);
                    if (forMultiplayer && excluded.Count > 0)
                    {
                        var note = zip.CreateEntry("multiplayer-missing-files.txt");
                        var noteStream = note.Open();
                        await using var noteDisposal = noteStream.ConfigureAwait(false);
                        var writer = new StreamWriter(noteStream, System.Text.Encoding.UTF8, leaveOpen: true);
                        await using var writerDisposal = writer.ConfigureAwait(false);
                        await writer.WriteLineAsync("Required files to supply manually. Match the following path, size and hashes before joining.".AsMemory(), cancellationToken).ConfigureAwait(false);
                        foreach (var declaration in references.Concat(manualReferences).OfType<JsonObject>().Where(item => item["path"] is not null && (item["downloads"] is not JsonArray urls || urls.Count == 0)))
                            await writer.WriteLineAsync(declaration.ToJsonString().AsMemory(), cancellationToken).ConfigureAwait(false);
                    }

                    foreach (var file in PackFiles.Enumerate(instance.GameDirectory, relative => ExportDirectory(relative, owned.Keys), allowReadLinks: true))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(instance.GameDirectory, file).Replace('\\', '/');
                        var first = relative.Split('/')[0];
                        if (forMultiplayer && !MultiplayerFile(relative)) continue;
                        if (referenced.Contains(relative))
                            continue;
                        if (ExcludedTree(first))
                            continue;
                        if (relative is "launcher_accounts.json" or "launcher_profiles.json" or "accounts.json" or "servers.dat")
                            continue;
                        if (!owned.ContainsKey(relative) && first is not ("config" or "defaultconfigs" or "scripts" or "kubejs" or "mods" or "resourcepacks" or "shaderpacks") && relative is not ("options.txt" or "optionsof.txt"))
                            continue;
                        if (!includeLocalMods && (first is "mods" or "resourcepacks" or "shaderpacks" || file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
                        {
                            excluded.Add(new(relative, "No redistributable platform reference; omitted unless --include-local-mods explicitly authorizes bundling."));
                            continue;
                        }

                        var entry = zip.CreateEntry("overrides/" + relative, CompressionLevel.Fastest);
                        var input = File.OpenRead(file);
                        await using var inputDisposal = input.ConfigureAwait(false);
                        var target = entry.Open();
                        await using var targetDisposal = target.ConfigureAwait(false);
                        await input.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                        overrides++;
                    }

                    // Extra metadata carries only pack ownership, never account or launcher settings.
                    var exportLock = new JsonObject
                    {
                        ["schemaVersion"] = 1,
                        ["dependencies"] = dependencies.DeepClone(),
                        ["files"] = Array(owned.Where(pair => referenced.Contains(pair.Key) || (includeLocalMods && pair.Key.StartsWith("mods/"))).Select(pair => pair.Value))
                    };
                    await WriteJsonEntryAsync(zip, "rodel.pack-lock.json", exportLock, cancellationToken).ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
            return new(destination, format, references.Count, overrides, excluded, excluded.Count == 0);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private async Task<JsonObject> ExportDependenciesAsync(InstanceRecord instance, JsonObject packLock, CancellationToken cancellationToken)
    {
        var dependencies = packLock["dependencies"]?.DeepClone() as JsonObject ?? new JsonObject { ["minecraft"] = instance.VersionId };
        try
        {
            var selected = new SettingsService(_paths).GetEffective(instance).Values["versionId"]?.GetValue<string>() ?? instance.VersionId;
            if (selected is null)
                return dependencies;
            var runtime = await RuntimeProfile.DescribeAsync(instance.MinecraftDirectory, selected, cancellationToken).ConfigureAwait(false);
            dependencies["minecraft"] = runtime.MinecraftVersion;
            foreach (var key in new[] { "fabric-loader", "quilt-loader", "forge", "neoforge" })
                dependencies.Remove(key);
            if (runtime.Loader is { } loader && runtime.LoaderVersion is { } version)
            {
                if (loader is "forge" or "neoforge" && version.StartsWith(runtime.MinecraftVersion + "-", StringComparison.Ordinal))
                    version = version[(runtime.MinecraftVersion.Length + 1)..];
                var key = loader switch { "fabric" => "fabric-loader", "quilt" => "quilt-loader", _ => loader };
                dependencies[key] = version;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            // Content-only and manually configured instances may not have readable runtime metadata yet.
            // Keep their original pack declaration rather than blocking an otherwise usable content export.
        }

        return dependencies;
    }

    private static bool ExcludedTree(string name) => name.ToLowerInvariant() is ".rodel" or ".rodel-instance-owner" or "saves" or "logs" or "crash-reports" or "screenshots" or "assets" or "libraries" or "versions" or "natives";
    private static bool MultiplayerFile(string relative)
    {
        var normalized = relative.Replace('\\', '/').ToLowerInvariant();
        var first = normalized.Split('/')[0];
        return first is "config" or "defaultconfigs" or "scripts" or "kubejs" ||
            (first is "mods" or "resourcepacks" or "shaderpacks" && !normalized.EndsWith(".disabled", StringComparison.Ordinal));
    }
    private static bool ExportDirectory(string relative, IEnumerable<string> owned)
    {
        var first = relative.Split('/')[0].ToLowerInvariant();
        if (ExcludedTree(first))
            return false;
        if (first is "config" or "defaultconfigs" or "scripts" or "kubejs" or "mods" or "resourcepacks" or "shaderpacks")
            return true;
        return owned.Any(path => path.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WriteJsonEntryAsync(ZipArchive zip, string name, JsonObject json, CancellationToken token)
    {
        var stream = zip.CreateEntry(name).Open();
        await using var streamDisposal = stream.ConfigureAwait(false);
        var writer = new StreamWriter(stream);
        await using var writerDisposal = writer.ConfigureAwait(false);
        await writer.WriteAsync(json.ToJsonString().AsMemory(), token).ConfigureAwait(false);
    }
}