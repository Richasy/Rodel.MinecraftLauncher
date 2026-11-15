// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Packs;

internal sealed class PackArchive(LauncherPaths paths, HttpClient client, ICurseForgeProvider? curseForgeProvider, IProgress<DownloadProgress>? progress = null)
{
    internal static PackInspection Inspect(string file)
    {
        PackFiles.NoLinks(file);
        using var zip = ZipFile.OpenRead(file);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            var relative = entry.FullName.TrimEnd('/');
            if (relative.Length == 0)
                continue;
            // Archive entry names are declarations, not paths in the machine's temporary directory.
            // Physical link checks belong to the selected destination immediately before writing.
            _ = PackFiles.SafeRead(Path.GetTempPath(), relative, allowInternal: true);
            if (!seen.Add(entry.FullName))
                throw new InvalidDataException("Duplicate pack archive entry.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Symbolic links in packs are not extracted.");
        }

        var mr = zip.GetEntry("modrinth.index.json");
        var cf = zip.GetEntry("manifest.json");
        var hmcl = zip.GetEntry("server-manifest.json");
        if (mr is null && cf is null && hmcl is null)
            throw new InvalidDataException("Not a mrpack, CurseForge or HMCL server modpack archive.");
        var format = mr is not null ? "mrpack" : cf is not null ? "curseforge" : "hmcl";
        using var stream = (mr ?? cf ?? hmcl)!.Open();
        var manifest = JsonNode.Parse(stream) as JsonObject ?? throw new InvalidDataException("Pack manifest must be an object.");
        if (format == "mrpack" && (manifest["formatVersion"]?.GetValue<int>() != 1 || manifest["game"]?.GetValue<string>() != "minecraft"))
            throw new InvalidDataException("Unsupported mrpack format or game.");
        if (format == "curseforge" && manifest["manifestType"]?.GetValue<string>() != "minecraftModpack")
            throw new InvalidDataException("Not a CurseForge Minecraft modpack manifest.");
        if (manifest["files"] is not JsonArray)
            throw new InvalidDataException("Pack manifest has no files array.");
        var dependencies = format switch
        {
            "mrpack" => (manifest["dependencies"] as JsonObject ?? []).ToDictionary(item => item.Key, item => item.Value?.DeepClone()),
            "hmcl" => HmclPack.Dependencies(manifest).ToDictionary(item => item.Key, item => (JsonNode?)JsonValue.Create(item.Value)),
            _ => CurseDependencies(manifest)
        };
        if (format == "hmcl")
            _ = HmclPack.Files(manifest);
        var optional = new List<PackFileDeclaration>();
        if (format == "mrpack")
        {
            foreach (var entry in manifest["files"]!.AsArray())
            {
                if (entry?["env"]?["client"]?.GetValue<string>() == "optional")
                    optional.Add(new(entry!.AsObject()));
            }
        }

        var minecraft = dependencies.GetValueOrDefault("minecraft")?.GetValue<string>() ?? throw new InvalidDataException("Pack does not declare its Minecraft version.");
        return new PackInspection
        {
            Format = format,
            Name = manifest["name"]?.DeepClone(),
            VersionId = (manifest["versionId"] ?? manifest["version"])?.DeepClone(),
            MinecraftVersion = minecraft,
            Dependencies = dependencies,
            OptionalFiles = optional,
            Manifest = manifest,
            ArchivePath = Path.GetFullPath(file)
        };
    }

    internal async Task<PackContentState> PrepareAsync(string archiveFile, string destination, bool download, string? manualDirectory, CancellationToken cancellationToken, PackOptionalState? optionalSelection = null, string? reuseDirectory = null, bool extractOverrides = true, IReadOnlyList<PackFileDeclaration>? onlyDeclarations = null)
    {
        var info = Inspect(archiveFile);
        var manifest = info.Manifest;
        var format = info.Format;
        var declaredFiles = format == "hmcl" ? HmclPack.Files(manifest)
            : [.. manifest["files"]!.AsArray().Select(node => new PackFileDeclaration(node as JsonObject ?? throw new InvalidDataException("Pack file declaration must be an object.")))];
        if (format == "curseforge" && manifest["rodelMissingFiles"] is JsonArray requirements)
            declaredFiles = [.. declaredFiles, .. requirements.Select(node => new PackFileDeclaration(node as JsonObject ?? throw new InvalidDataException("Manual requirement must be an object.")))];
        optionalSelection ??= new PackOptionalState();
        var selectedDeclarations = onlyDeclarations ?? declaredFiles;
        var optional = new PackOptionalChoice?[selectedDeclarations.Count];
        var missing = new PackMissingFile?[selectedDeclarations.Count];
        var skipped = new PackSkippedFile?[selectedDeclarations.Count];
        var records = new PackFileRecord?[selectedDeclarations.Count];
        var recoveryPaths = new string?[selectedDeclarations.Count];
        var archiveFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var owned = new Dictionary<string, PackFileRecord>(StringComparer.OrdinalIgnoreCase);
        var declarations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var declarationGate = new Lock();
        using var archiveGate = new SemaphoreSlim(1, 1);
        Func<Uri, IReadOnlyDictionary<string, string>>? downloadHeaders = curseForgeProvider is null ? null : curseForgeProvider.GetDownloadHeaders;
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(archiveFile);
        string[] prefixes = !extractOverrides ? [] : format == "mrpack" ? ["overrides/", "client-overrides/"]
            : [(manifest["overrides"]?.GetValue<string>() ?? "overrides").TrimEnd('/') + "/"];
        var overrides = prefixes.SelectMany(prefix => zip.Entries
            .Where(entry => entry.FullName.StartsWith(prefix, StringComparison.Ordinal) && !entry.FullName.EndsWith('/'))
            .Select(entry => (Entry: entry, Relative: entry.FullName[prefix.Length..]))).ToArray();
        long? ProgressSize(PackFileDeclaration file) => format == "curseforge" ? null
            : file.ClientEnvironment == "unsupported" || (file.ClientEnvironment == "optional" && !optionalSelection.Includes(file.Path!)) ? 0L
            : file.FileSize;
        var acquisition = new PackAcquisitionProgress(progress, [.. selectedDeclarations.Select(ProgressSize), .. overrides.Select(item => (long?)item.Entry.Length)]);
        // Validate colliding declarations before any parallel writes, and keep source order in retained results.
        foreach (var declaration in selectedDeclarations)
        {
            if ((format is "mrpack" or "hmcl" || declaration.Sha1 is not null || declaration.Sha512 is not null)
                && declaration.Path is { } relative && !declarations.Add(relative))
            {
                throw new InvalidDataException($"Duplicate declared pack file: {relative}");
            }
        }

        var embeddedFiles = format == "hmcl" ? selectedDeclarations.ToDictionary(file => file.Path!, file => zip.GetEntry("overrides/" + file.Path), StringComparer.OrdinalIgnoreCase) : [];
        progress?.Report(new(destination, 0, null, false) { OverallPercent = 0d });
        var parallel = new ParallelOptions
        {
            // The optional provider contract does not require thread-safe metadata calls.
            MaxDegreeOfParallelism = format == "curseforge" ? 1 : NetworkSettings.Concurrency(paths, 8),
            CancellationToken = cancellationToken
        };
        await Parallel.ForEachAsync(Enumerable.Range(0, selectedDeclarations.Count), parallel, async (index, token) =>
        {
            var declaration = selectedDeclarations[index];
            var batch = acquisition.ForFile(index);
            var completionPath = destination;
            token.ThrowIfCancellationRequested();
            var entry = declaration.ToMetadata();
            if (format is "mrpack" or "hmcl" || (declaration.Path is not null && (declaration.Sha1 is not null || declaration.Sha512 is not null)))
            {
                var relative = declaration.Path!;
                var target = PackFiles.Safe(destination, relative);
                completionPath = target;
                if (format == "hmcl" && reuseDirectory is not null && HmclPack.HasDisabledVariant(reuseDirectory, relative))
                {
                    skipped[index] = new(declaration, "locally-disabled");
                    batch.Complete(target);
                    return;
                }

                if (declaration.ClientEnvironment == "unsupported")
                {
                    skipped[index] = new(declaration, "client-unsupported");
                    batch.Complete(target);
                    return;
                }

                if (declaration.ClientEnvironment == "optional")
                {
                    bool selected;
                    lock (declarationGate)
                    {
                        selected = optionalSelection.Includes(relative);
                        optionalSelection.Paths[relative] = selected;
                    }

                    optional[index] = new(relative, selected);
                    if (!selected)
                    {
                        skipped[index] = new(declaration, "optional-excluded");
                        batch.Complete(target);
                        return;
                    }
                }

                var sha1 = declaration.Sha1;
                var sha512 = declaration.Sha512;
                if (string.IsNullOrWhiteSpace(sha1) && string.IsNullOrWhiteSpace(sha512))
                    throw new InvalidDataException($"mrpack file has no verification hash: {relative}");
                var local = manualDirectory is not null ? PackFiles.Safe(manualDirectory, relative) : null;
                var failure = "No permitted download succeeded; supply the file under --manual-directory using its declared relative path.";
                var acquired = false;
                var transferred = false;
                var reused = reuseDirectory is null ? null : PackFiles.Safe(reuseDirectory, relative);
                if (reused is not null && await DownloadService.ValidAsync(reused, sha1, declaration.FileSize, token).ConfigureAwait(false) && (sha512 is null || string.Equals(await PackFiles.Sha512Async(reused, token).ConfigureAwait(false), sha512, StringComparison.OrdinalIgnoreCase)))
                {
                    await PackFiles.PublishAsync(reused, target, token).ConfigureAwait(false);
                    acquired = true;
                }
                else if (local is not null && File.Exists(local))
                {
                    await PackFiles.PublishAsync(local, target, token).ConfigureAwait(false);
                    acquired = true;
                }
                else if (embeddedFiles.TryGetValue(relative, out var embedded) && embedded is not null)
                {
                    // ZipArchive entries share one underlying seekable stream.
                    await archiveGate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await PackFiles.ExtractAsync(embedded, target, batch, token).ConfigureAwait(false);
                        archiveFiles.Add(relative);
                    }
                    finally { archiveGate.Release(); }

                    acquired = true;
                }
                else if (download)
                {
                    recoveryPaths[index] = relative;
                    foreach (var url in declaration.Downloads)
                    {
                        try
                        {
                            var uri = new Uri(url);
                            if (uri.Scheme is not ("https" or "http"))
                                throw new InvalidDataException("Pack downloads require HTTP(S).");
                            transferred = await new DownloadService(paths, client).DownloadAsync(uri: uri, destination: target, sha1: sha1, size: declaration.FileSize, progress: batch, cancellationToken: token, headersForUri: downloadHeaders, sha512: sha512).ConfigureAwait(false);
                            acquired = true;
                            break;
                        }
                        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException)
                        {
                            failure = e.Message;
                        }
                    }
                }

                if (acquired)
                {
                    var source = entry["rodelSource"] as JsonObject;
                    var platform = source is not null ? SourceString(source, "platform") : format == "mrpack" && declaration.Downloads.Any() ? "modrinth" : null;
                    var record = await RecordAsync(relative, target, entry, platform, token, batch, sha1, sha512, declaration.FileSize).ConfigureAwait(false);
                    if (source is not null)
                    {
                        record.ProjectId = SourceString(source, "projectId");
                        record.VersionId = SourceString(source, "versionId");
                        record.IdentityVerified = false; // A sender's source claim is not publisher verification.
                    }

                    record.Acquisition = transferred ? "download" : "local";
                    records[index] = record;
                }
                else
                {
                    missing[index] = new() { Path = relative, State = "manual-download", Reason = failure, Metadata = entry };
                }
            }
            else
            {
                var project = declaration.ProjectId;
                var file = declaration.FileId;
                var pair = $"{project}:{file}";
                JsonObject? data = null;
                JsonObject? projectData = null;
                string? failure = null;
                if (curseForgeProvider is not null && download)
                {
                    try
                    {
                        data = await curseForgeProvider.GetFileAsync(project.ToString(), file.ToString(), token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is HttpRequestException or IOException or System.Text.Json.JsonException)
                    {
                        failure = e.Message;
                    }

                    if (data is not null)
                    {
                        try
                        {
                            projectData = await curseForgeProvider.GetProjectAsync(project.ToString(), token).ConfigureAwait(false);
                        }
                        catch (Exception e) when (e is HttpRequestException or IOException or System.Text.Json.JsonException)
                        {
                        }
                    }
                }

                var local = FindCurseManual(manualDirectory, pair, project, file, data?["fileName"]?.GetValue<string>());
                var name = data?["fileName"]?.GetValue<string>() ?? (local is not null ? Path.GetFileName(local) : $"{project}-{file}.jar");
                var directory = projectData?["classId"]?.GetValue<int>() switch
                {
                    12 => "resourcepacks",
                    6552 => "shaderpacks",
                    _ => "mods"
                };
                var relative = directory + "/" + name;
                var target = PackFiles.Safe(destination, relative);
                completionPath = target;
                var hash = (data?["hashes"] as JsonArray)?.FirstOrDefault(x => x?["algo"]?.GetValue<int>() == 1)?["value"]?.GetValue<string>();
                var acquired = false;
                var transferred = false;
                if (local is not null && File.Exists(local))
                {
                    await PackFiles.PublishAsync(local, target, token).ConfigureAwait(false);
                    acquired = true;
                }
                else if (download && data?["downloadUrl"]?.GetValue<string>() is { Length: > 0 } url)
                {
                    recoveryPaths[index] = relative;
                    try
                    {
                        transferred = await new DownloadService(paths, client).DownloadAsync(uri: new Uri(url), destination: target, sha1: hash, size: data["fileLength"]?.GetValue<long>(), progress: batch, cancellationToken: token, headersForUri: downloadHeaders).ConfigureAwait(false);
                        acquired = true;
                    }
                    catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException)
                    {
                        failure = e.Message;
                    }
                }

                if (acquired)
                {
                    var record = await RecordAsync(relative, target, entry, "curseforge", token, batch, hash, size: data?["fileLength"]?.GetValue<long>()).ConfigureAwait(false);
                    record.ProjectId = project.ToString();
                    record.VersionId = file.ToString();
                    record.RawDownloadUrl = data?["downloadUrl"]?.DeepClone();
                    record.IdentityVerified = hash is not null;
                    record.ClassificationVerified = projectData?["classId"] is not null;
                    if (projectData?["classId"] is null)
                        record.ClassificationWarning = "Project category is unavailable; mods was used as the compatibility fallback.";
                    if (hash is null)
                        record.Warning = "Local file accepted by explicit project:file mapping; platform identity could not be verified without API metadata.";
                    record.Acquisition = transferred ? "download" : "local";
                    records[index] = record;
                }
                else
                {
                    missing[index] = new()
                    {
                        ProjectId = project,
                        FileId = file,
                        State = "manual-download",
                        Reason = failure ?? (curseForgeProvider is null ? "CurseForge online support is not included in this build. Supply this file with --manual-directory." : "Author distribution restrictions or unavailable download URL."),
                        ReasonCode = curseForgeProvider is null ? "PLATFORM_UNAVAILABLE" : null,
                        ManualKey = pair,
                        ManualFileName = $"{project}-{file}.jar",
                        Metadata = entry
                    };
                }
            }

            batch.Complete(completionPath);
        }).ConfigureAwait(false);

        foreach (var record in records.OfType<PackFileRecord>())
            owned[record.Path] = record;

        // Only this attempt's known transfer sidecars need cleanup. Do not rescan the extracted tree
        // or remove an author's ordinary override merely because its name resembles a recovery file.
        foreach (var relative in recoveryPaths.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var suffix in new[] { ".rodel.partial", ".rodel.partial.json" })
            {
                var candidate = PackFiles.SafeRead(destination, relative + suffix);
                if (File.Exists(candidate))
                    File.Delete(PackFiles.Safe(destination, relative + suffix));
            }
        }

        var excludedPaths = declaredFiles.Where(item => item.Path is not null && (item.ClientEnvironment == "unsupported"
                || (item.ClientEnvironment == "optional" && !optionalSelection.Includes(item.Path))))
            .Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var prefix in prefixes)
            _ = PackFiles.SafeRead(destination, prefix.TrimEnd('/'));
        for (var index = 0; index < overrides.Length; index++)
        {
            var (entry, relative) = overrides[index];
            var batch = acquisition.ForFile(selectedDeclarations.Count + index);
            cancellationToken.ThrowIfCancellationRequested();
            if ((format == "hmcl" && declarations.Contains(relative)) || excludedPaths.Contains(relative))
            {
                batch.Complete(destination);
                continue;
            }

            var target = PackFiles.Safe(destination, relative);
            await PackFiles.ExtractAsync(entry, target, batch, cancellationToken).ConfigureAwait(false);
            owned[relative] = await RecordAsync(relative, target, new JsonObject { ["archiveEntry"] = entry.FullName }, null, cancellationToken, batch).ConfigureAwait(false);
            archiveFiles.Add(relative);
            batch.Complete(target);
        }

        return new PackContentState
        {
            Format = info.Format,
            Name = info.Name,
            VersionId = info.VersionId,
            MinecraftVersion = info.MinecraftVersion,
            Dependencies = info.Dependencies,
            OptionalFiles = info.OptionalFiles,
            Manifest = info.Manifest,
            ArchivePath = info.ArchivePath,
            Files = [.. owned.Values],
            Missing = [.. missing.OfType<PackMissingFile>()],
            Skipped = [.. skipped.OfType<PackSkippedFile>()],
            OptionalSelection = optionalSelection,
            OptionalChoices = [.. optional.OfType<PackOptionalChoice>()],
            Complete = missing.All(item => item is null),
            Acquisition = new(download, records.Count(item => item?.Acquisition == "local"), records.Count(item => item?.Acquisition == "download"),
                archiveFiles.Count, missing.Count(item => item is not null), skipped.Count(item => item is not null))
        };
    }

    private static string? FindCurseManual(string? directory, string pair, long project, long file, string? expectedName)
    {
        if (directory is null)
            return null;
        var mapFile = Path.Combine(directory, "manual-files.json");
        if (File.Exists(mapFile) && JsonFiles.ReadObject(mapFile)[pair]?.GetValue<string>() is { } mapped)
        {
            var resolved = Path.GetFullPath(mapped, directory);
            PackFiles.NoLinks(resolved);
            return resolved;
        }

        foreach (var name in new[]
        {
            expectedName,
            $"{project}-{file}.jar"}.Where(x => x is not null))
        {
            var candidate = PackFiles.Safe(directory, name!);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? SourceString(JsonObject source, string key) => source[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static async Task<PackFileRecord> RecordAsync(string relative, string file, JsonObject metadata, string? platform, CancellationToken token,
        PackAcquisitionProgress.FileProgress? progress = null, string? sha1 = null, string? sha512 = null, long? size = null)
    {
        PackFiles.NoLinks(file);
        var hashes = await FileIntegrity.ReadAsync(file, true, false, true, token,
            (bytes, total) => progress?.Report(new(file, bytes, total, false) { Stage = "pack-verify" })).ConfigureAwait(false);
        if ((!string.IsNullOrWhiteSpace(sha1) && !string.Equals(sha1, hashes.Sha1, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(sha512) && !string.Equals(sha512, hashes.Sha512, StringComparison.OrdinalIgnoreCase))
            || (size is not null && size != hashes.Size))
        {
            throw new InvalidDataException($"Pack checksum mismatch: {relative}");
        }

        return new()
        {
            Path = relative,
            Kind = PackFiles.Kind(relative),
            Enabled = true,
            Sha1 = hashes.Sha1,
            Sha512 = hashes.Sha512,
            Size = hashes.Size,
            Platform = platform,
            Metadata = (JsonObject)metadata.DeepClone(),
            RawDownloadUrl = (metadata["downloads"] as JsonArray)?.FirstOrDefault()?.DeepClone()
        };
    }
    internal static Dictionary<string, JsonNode?> CurseDependencies(JsonObject manifest)
    {
        var result = new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["minecraft"] = manifest["minecraft"]?["version"]?.DeepClone()
        };
        if (manifest["minecraft"]?["modLoaders"] is JsonArray loaders)
        {
            foreach (var loader in loaders)
            {
                var id = loader?["id"]?.GetValue<string>();
                if (id is null)
                    continue;
                var separator = id.IndexOf('-');
                if (separator > 0)
                {
                    result[id[..separator] switch
                    {
                        "fabric" => "fabric-loader",
                        "quilt" => "quilt-loader",
                        var other => other
                    }] = id[(separator + 1)..];
                }
            }
        }

        return result;
    }
}