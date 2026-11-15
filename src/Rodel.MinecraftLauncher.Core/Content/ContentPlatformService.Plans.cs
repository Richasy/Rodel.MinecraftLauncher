// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Content;

public sealed partial class ContentPlatformService
{
    /// <summary>Resolves one content project's dependency plan against the current local tracking baseline without installing files.</summary>
    public Task<ContentInstallPlan> PlanInstallAsync(string platform, string projectId, string? versionId, string game, string kind = "mods", string? world = null, string? minecraftVersion = null, string? loader = null, ContentInstallOptions? options = null, CancellationToken ct = default)
        => PlanInstallProjectsAsync(platform, [(projectId, versionId, options?.Channel)], game, kind, world, minecraftVersion, loader, options, ct);

    internal async Task<ContentInstallPlan> PlanInstallProjectsAsync(string platform, IReadOnlyList<(string ProjectId, string? VersionId, string? Channel)> projects,
        string game, string kind, string? world, string? minecraftVersion, string? loader, ContentInstallOptions? options, CancellationToken ct)
    {
        if (projects.Count == 0) throw new ArgumentException("Select at least one project.", nameof(projects));
        platform = Normalize(platform);
        if (platform == "curseforge")
            _ = CurseForge;
        options ??= new();
        ValidateChannel(options.Channel);
        var baseline = await ContentService.LockFingerprintAsync(game, ct).ConfigureAwait(false);
        if (options.ExpectedLockFingerprint is { } expected && !string.Equals(expected, baseline, StringComparison.OrdinalIgnoreCase))
            throw new LauncherOperationException("CONTENT_BASELINE_CHANGED", "Content tracking changed since the reviewed plan. Read a new plan and retain any explicit version choices that are still intended.", new JsonObject { ["expectedLockFingerprint"] = expected, ["actualLockFingerprint"] = baseline, ["localRetained"] = true });
        var contentLock = await _content.GetLockAsync(game, ct).ConfigureAwait(false);
        if (baseline != await ContentService.LockFingerprintAsync(game, ct).ConfigureAwait(false))
            throw new IOException("Content changed while reading its baseline.");
        var nodes = new List<ContentPlanNode>();
        var edges = new List<ContentPlanEdge>();
        var conflicts = new List<ContentPlanFinding>();
        var warnings = new List<ContentPlanFinding>();
        var chosen = new Dictionary<string, ContentPlanNode>(StringComparer.Ordinal);
        var localInventories = new Dictionary<string, IReadOnlyList<ContentEntry>>(StringComparer.Ordinal);
        var preferredFixed = new Dictionary<string, string>(StringComparer.Ordinal);
        var fixedThisPass = new Dictionary<string, string>(StringComparer.Ordinal);
        var preferredChannels = new Dictionary<string, string?>(StringComparer.Ordinal);
        var channelsThisPass = new Dictionary<string, string?>(StringComparer.Ordinal);
        var primary = string.Empty;
        var needsReplan = false;
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            nodes.Clear();
            edges.Clear();
            conflicts.Clear();
            warnings.Clear();
            chosen.Clear();
            fixedThisPass.Clear();
            channelsThisPass.Clear();
            primary = string.Empty;
            needsReplan = false;
            foreach (var (ProjectId, VersionId, Channel) in projects)
            {
                var resolved = await Resolve(ProjectId, VersionId, false, null, Channel).ConfigureAwait(false);
                if (primary.Length == 0) primary = resolved;
            }

            if (conflicts.Count > 0 || !needsReplan)
                break;
            var signature = string.Join("|", fixedThisPass.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + x.Value))
                + ";channels=" + string.Join("|", channelsThisPass.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + x.Value));
            if (preferredFixed.Count == fixedThisPass.Count && preferredFixed.All(x => fixedThisPass.GetValueOrDefault(x.Key) == x.Value)
                && preferredChannels.Count == channelsThisPass.Count && preferredChannels.All(x => channelsThisPass.GetValueOrDefault(x.Key) == x.Value))
            {
                break;
            }

            if (!signatures.Add(signature))
            {
                conflicts.Add(new() { Reason = "dependency-metadata-does-not-converge", Resolution = "Choose exact project versions or use --no-dependencies with manually managed files." });
                break;
            }

            preferredFixed = new Dictionary<string, string>(fixedThisPass, StringComparer.Ordinal);
            preferredChannels = new Dictionary<string, string?>(channelsThisPass, StringComparer.Ordinal);
        }

        // Preserve exact known requirements of active consumers that this operation retains.
        // Local unknown jars and disabled consumers are deliberately not inferred constraints.
        foreach (var consumer in (contentLock["files"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (consumer["platform"]?.GetValue<string>() != platform || consumer["enabled"]?.GetValue<bool>() == false || consumer["projectId"]?.GetValue<string>() is not { } consumerProject || consumer["path"]?.GetValue<string>() is not { } consumerPath)
                continue;
            if (consumer["kind"]?.ToString() == "mods"
                && (consumer["directory"]?.GetValue<bool>() == true || !ContentService.IsModFileName(Path.GetFileName(consumerPath))))
            {
                continue;
            }

            var fullConsumer = Path.GetFullPath(Path.IsPathRooted(consumerPath) ? consumerPath : Path.Combine(game, consumerPath));
            if (!File.Exists(fullConsumer))
                continue;
            if (chosen.TryGetValue(consumerProject, out var replacement) && string.Equals(Path.GetDirectoryName(fullConsumer), ContentService.DirectoryFor(game, replacement.Kind, replacement.World), StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var dependency in (consumer["upstream"]?["dependencies"] as JsonArray ?? consumer["dependencies"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (dependency["dependency_type"]?.GetValue<string>() != "required" && dependency["relationType"]?.GetValue<int>() != 3 && dependency["projectId"] is null)
                    continue;
                var requiredVersion = dependency["version_id"]?.GetValue<string>() ?? dependency["versionId"]?.GetValue<string>() ?? dependency["fileId"]?.ToString();
                if (requiredVersion is null)
                    continue;
                var dependencyProject = dependency["project_id"]?.GetValue<string>() ?? dependency["projectId"]?.GetValue<string>() ?? dependency["modId"]?.ToString();
                if (dependencyProject is null && platform == "modrinth")
                {
                    try
                    {
                        dependencyProject = (await VersionAsync(string.Empty, requiredVersion, null, null, null, platform, ct).ConfigureAwait(false))["project_id"]?.GetValue<string>();
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        warnings.Add(new() { Reason = "retained-dependency-metadata-unavailable", RetainedConsumer = consumerProject, VersionId = requiredVersion });
                        continue;
                    }
                }

                if (dependencyProject is null || !chosen.TryGetValue(dependencyProject, out var target) || target.VersionId == requiredVersion)
                    continue;
                var finding = new ContentPlanFinding
                {
                    Reason = "retained-consumer-fixed-version",
                    RetainedConsumer = consumerProject,
                    ConsumerPath = consumerPath,
                    ProjectId = dependencyProject,
                    RequiredVersion = requiredVersion,
                    SelectedVersion = target.VersionId,
                    Resolution = "Update the consumer too, choose --select-version explicitly, or manage files manually."
                };
                if (!options.PreserveRetainedConsumers && options.VersionSelections?.ContainsKey(dependencyProject) == true)
                {
                    finding.Reason = "explicit-selection-overrides-retained-consumer";
                    warnings.Add(finding);
                }
                else
                {
                    conflicts.Add(finding);
                }
            }
        }

        return new ContentInstallPlan
        {
            Platform = platform,
            PrimaryProject = primary,
            PrimaryProjects = [.. projects.Select(project => project.ProjectId)],
            GameDirectory = Path.GetFullPath(game),
            EffectiveChannel = options.Channel ?? "any",
            LockFingerprint = baseline,
            Nodes = nodes,
            Edges = edges,
            Conflicts = conflicts,
            Warnings = warnings,
            CanApply = conflicts.Count == 0
        };
        string? RestrictChannel(string? first, string? second) => first == "release" || second == "release" ? "release"
            : first == "beta" || second == "beta" ? "beta" : null;
        void RequireChannel(string project, string? channel)
        {
            var required = RestrictChannel(channelsThisPass.GetValueOrDefault(project), channel);
            channelsThisPass[project] = required;
            if (chosen.TryGetValue(project, out var existing) && !fixedThisPass.ContainsKey(project)
                && !AllowedChannel(existing.Upstream["version_type"]?.ToString(), required))
            {
                needsReplan = true;
            }
        }

        async Task<string> Resolve(string requestedProject, string? requestedVersion, bool dependency, string? parent, string? requestedChannel)
        {
            ValidateChannel(requestedChannel);
            RequireChannel(requestedProject, requestedChannel);
            var channel = RestrictChannel(requestedChannel, preferredChannels.GetValueOrDefault(requestedProject));
            var selection = options.VersionSelections?.GetValueOrDefault(requestedProject);
            if (requestedVersion is null && selection is null && chosen.TryGetValue(requestedProject, out var already))
            {
                edges.Add(new ContentPlanEdge { RequiredBy = parent, ProjectId = requestedProject, SelectedVersion = already.VersionId });
                return requestedProject;
            }

            var version = await VersionAsync(requestedProject, selection ?? requestedVersion ?? preferredFixed.GetValueOrDefault(requestedProject), channel, minecraftVersion, loader, platform, ct).ConfigureAwait(false);
            var project = platform == "modrinth" ? version["project_id"]?.GetValue<string>() ?? requestedProject : version["modId"]?.ToString() ?? requestedProject;
            RequireChannel(project, requestedChannel);
            var id = version["id"]?.ToString() ?? throw new InvalidDataException("Content version has no ID.");
            if (requestedProject.Length > 0 && (selection is not null || requestedVersion is not null) && project != requestedProject)
            {
                var requestedInfo = await GetProjectAsync(platform, requestedProject, ct).ConfigureAwait(false);
                if (requestedInfo["id"]?.ToString() != project)
                    throw new InvalidDataException($"Selected version {id} belongs to {project}, not {requestedProject}.");
            }

            // Version-only dependencies can still be overridden after discovering their project identity.
            if (selection is null && options.VersionSelections?.TryGetValue(project, out var selected) == true && selected != id)
            {
                version = await VersionAsync(project, selected, channel, minecraftVersion, loader, platform, ct).ConfigureAwait(false);
                if ((platform == "modrinth" ? version["project_id"]?.ToString() : version["modId"]?.ToString()) != project)
                    throw new InvalidDataException("Selected dependency belongs to another project.");
                id = version["id"]!.ToString();
                selection = selected;
            }

            if (selection is not null && requestedVersion is not null && selection != requestedVersion)
                warnings.Add(new() { ProjectId = project, RequiredBy = parent, RequestedVersion = requestedVersion, SelectedVersion = selection, Reason = "explicit-version-selection" });
            if (requestedVersion is not null || selection is not null)
            {
                if (fixedThisPass.TryGetValue(project, out var fixedVersion) && fixedVersion != id)
                    conflicts.Add(new() { ProjectId = project, FirstVersion = fixedVersion, RequestedVersion = id, RequiredBy = parent, Reason = "different-fixed-versions", Resolution = "Use --select-version PROJECT=VERSION or manage dependencies with --no-dependencies." });
                else
                    fixedThisPass[project] = id;
            }

            edges.Add(new ContentPlanEdge { RequiredBy = parent, ProjectId = project, RequestedVersion = requestedVersion, SelectedVersion = id });
            if (chosen.TryGetValue(project, out var old))
            {
                if (old.VersionId != id && (requestedVersion is not null || selection is not null))
                    needsReplan = true;
                return project;
            }

            if (requestedVersion is not null || selection is not null)
            {
                if (minecraftVersion is not null && version["game_versions"] is JsonArray supported && !supported.Any(x => x?.GetValue<string>() == minecraftVersion))
                    warnings.Add(new() { ProjectId = project, Reason = "explicit-version-game-metadata-mismatch", VersionId = id });
                if (!AllowedChannel(version["version_type"]?.GetValue<string>(), channel))
                    warnings.Add(new() { ProjectId = project, Reason = "explicit-version-channel-override", VersionId = id });
            }

            var pinned = (contentLock["files"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(x => x["projectId"]?.GetValue<string>() == project && x["platform"]?.GetValue<string>() == platform && x["pinned"]?.GetValue<bool>() == true
                && (x["kind"]?.ToString() != "mods" || (x["directory"]?.GetValue<bool>() != true && ContentService.IsModFileName(Path.GetFileName(x["path"]?.ToString() ?? x["fileName"]?.ToString() ?? string.Empty)))));
            if (pinned is not null && pinned["versionId"]?.GetValue<string>() != id && dependency && selection is null)
                conflicts.Add(new() { ProjectId = project, Reason = "pinned-dependency", PinnedVersion = pinned["versionId"]!.GetValue<string>(), RequestedVersion = id });
            minecraftVersion ??= version["game_versions"]?.AsArray().FirstOrDefault()?.GetValue<string>();
            loader ??= version["loaders"]?.AsArray().FirstOrDefault()?.GetValue<string>();
            var selectedKind = kind;
            if (dependency)
            {
                var info = await GetProjectAsync(platform, project, ct).ConfigureAwait(false);
                selectedKind = platform == "modrinth" ? info["project_type"]?.GetValue<string>() switch
                {
                    "resourcepack" => "resourcepacks",
                    "shader" => "shaderpacks",
                    _ => "mods"
                } : info["classId"]?.GetValue<int>() switch
                {
                    12 => "resourcepacks",
                    6552 => "shaderpacks",
                    _ => "mods"
                };
                if (version["loaders"]?.AsArray().Any(x => x?.GetValue<string>() == "datapack") == true)
                    selectedKind = "datapacks";
            }

            // Modrinth also uses loaders for resource/shader pack formats (minecraft, iris, etc.).
            // Only actual mods are constrained by the instance's mod loader, including dependencies.
            if (selectedKind == "mods" && (requestedVersion is not null || selection is not null)
                && loader is not null && version["loaders"] is JsonArray loaders
                && !loaders.Any(x => x?.GetValue<string>() == loader))
            {
                warnings.Add(new() { ProjectId = project, Reason = "explicit-version-loader-metadata-mismatch", VersionId = id });
            }

            _ = ContentService.DirectoryFor(game, selectedKind, selectedKind == "datapacks" ? world : null);
            var file = platform == "modrinth" ? (version["files"] as JsonArray)?.FirstOrDefault(x => x?["primary"]?.GetValue<bool>() == true) ?? (version["files"] as JsonArray)?.FirstOrDefault() : version;
            var filename = file?[platform == "modrinth" ? "filename" : "fileName"]?.GetValue<string>();
            if (platform == "modrinth" && filename?.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) == true)
                throw new LauncherOperationException("PACK_ROUTE_REQUIRED", "This is a modpack archive. Use pack install PROJECT [--pack-version VERSION] to create a pack instance, or pack update-online INSTANCE --project PROJECT.");
            if (string.IsNullOrWhiteSpace(filename) || Path.GetFileName(filename) != filename || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException($"Content version has no safe file name: {project}/{id}");
            if (!ContentService.IsContentFileName(selectedKind, filename))
                conflicts.Add(new() { ProjectId = project, VersionId = id, Reason = "not-a-content-package" });
            var url = file?[platform == "modrinth" ? "url" : "downloadUrl"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(url))
                conflicts.Add(new() { ProjectId = project, VersionId = id, Reason = "manual-download-required" });
            var node = new ContentPlanNode
            {
                Platform = platform,
                ProjectId = project,
                VersionId = id,
                FileName = filename,
                Kind = selectedKind,
                World = selectedKind == "datapacks" ? world : null,
                DownloadUrl = url,
                Sha1 = platform == "modrinth" ? file?["hashes"]?["sha1"]?.GetValue<string>() : version["hashes"]?.AsArray().FirstOrDefault(x => x?["algo"]?.GetValue<int>() == 1)?["value"]?.GetValue<string>(),
                Sha512 = platform == "modrinth" ? file?["hashes"]?["sha512"]?.GetValue<string>() : null,
                Size = file?[platform == "modrinth" ? "size" : "fileLength"]?.GetValue<long>(),
                GameVersion = minecraftVersion,
                Loader = loader,
                Dependency = dependency,
                Dependencies = version["dependencies"]?.DeepClone(),
                Upstream = (JsonObject)version.DeepClone()
            };
            var directory = ContentService.DirectoryFor(game, selectedKind, selectedKind == "datapacks" ? world : null);
            var existingFiles = new List<JsonObject>();
            foreach (var existing in (contentLock["files"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (existing["platform"]?.GetValue<string>() != platform || existing["projectId"]?.GetValue<string>() != project || existing["kind"]?.GetValue<string>() != selectedKind)
                    continue;
                var oldPath = existing["path"]?.GetValue<string>();
                if (oldPath is null)
                    continue;
                if (existing["directory"]?.GetValue<bool>() == true || !ContentService.IsContentFileName(selectedKind, Path.GetFileName(oldPath)))
                    continue;
                var full = Path.GetFullPath(Path.IsPathRooted(oldPath) ? oldPath : Path.Combine(game, oldPath));
                if (!string.Equals(Path.GetDirectoryName(full), directory, StringComparison.OrdinalIgnoreCase))
                    continue;
                existingFiles.Add((JsonObject)existing.DeepClone());
                if (File.Exists(full) && !await DownloadService.ValidAsync(full, existing["sha1"]?.GetValue<string>(), existing["size"]?.GetValue<long>(), ct).ConfigureAwait(false))
                    conflicts.Add(new() { ProjectId = project, Path = oldPath, Reason = "locally-modified-content", Resolution = "Keep the edited file or import a chosen replacement separately." });
            }

            node.ExistingFiles = existingFiles;
            node.Change = existingFiles.Count == 0 ? "add" : existingFiles.All(x => x["versionId"]?.GetValue<string>() == id) ? "reuse-or-repair" : "replace";
            if (node.Sha1 is { } expectedHash)
            {
                var inventoryKey = selectedKind + "\0" + world;
                if (!localInventories.TryGetValue(inventoryKey, out var inventory))
                    localInventories[inventoryKey] = inventory = await _content.ListAsync(game, selectedKind, world, ct).ConfigureAwait(false);
                foreach (var local in inventory
                    .Where(item => item.IsContent
                        && string.Equals(item.Sha1, expectedHash, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(item => item.Enabled))
                {
                    var owner = local.Tracking;
                    if (owner?["projectId"]?.ToString() is { } ownerProject
                        && (ownerProject != project || owner["platform"]?.ToString() != platform))
                    {
                        conflicts.Add(new() { ProjectId = project, Path = local.Path, Reason = "identical-file-has-different-source" });
                        continue;
                    }

                    var localPath = Path.GetFullPath(Path.Combine(game, local.Path));
                    if (!await DownloadService.ValidAsync(localPath, expectedHash, node.Size, ct).ConfigureAwait(false))
                        throw new IOException($"Local content changed while planning: {localPath}");
                    if (node.Sha512 is { } expectedSha512)
                    {
                        var stream = File.OpenRead(localPath);
                        await using var streamDisposal = stream.ConfigureAwait(false);
                        var actual = Convert.ToHexStringLower(await SHA512.HashDataAsync(stream, ct).ConfigureAwait(false));
                        if (!string.Equals(actual, expectedSha512, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    node.ReuseLocal = local;
                    if (existingFiles.Count == 0) node.Change = "reuse-local";
                    break;
                }
            }

            chosen.Add(project, node); // Register before recursion: cycles terminate without dropping provenance edges.
            foreach (var dep in version["dependencies"] as JsonArray ?? [])
            {
                if (dep is null)
                    continue;
                var relation = platform == "modrinth" ? dep["dependency_type"]?.GetValue<string>() : dep["relationType"]?.GetValue<int>() == 3 ? "required" : "optional";
                if (relation != "required")
                {
                    if (relation is not ("optional" or "embedded"))
                        warnings.Add(new() { RequiredBy = project, Relation = relation, Dependency = dep.DeepClone() });
                    continue;
                }

                if (!options.IncludeDependencies)
                {
                    warnings.Add(new() { RequiredBy = project, Reason = "dependencies-managed-manually", Dependency = dep.DeepClone() });
                    continue;
                }

                var dp = platform == "modrinth" ? dep["project_id"]?.GetValue<string>() : dep["modId"]?.ToString();
                var dv = platform == "modrinth" ? dep["version_id"]?.GetValue<string>() : dep["fileId"]?.ToString();
                if (dp is null && dv is null)
                    warnings.Add(new() { RequiredBy = project, Reason = "manual-external-dependency", Dependency = dep.DeepClone() });
                else
                    await Resolve(dp ?? string.Empty, dv, true, project, channel).ConfigureAwait(false);
            }

            nodes.Add(node);
            return project;
        }
    }

    private async Task<JsonObject> VersionAsync(string project, string? id, string? channel, string? game, string? loader, string platform, CancellationToken ct)
    {
        if (id is not null)
            return (platform == "curseforge" ? await CurseForge.GetFileAsync(project, id, ct).ConfigureAwait(false) : await ApiAsync("version/" + Encode(id), ct).ConfigureAwait(false)).DeepClone() as JsonObject ?? throw new InvalidDataException($"Version not found: {id}");
        var versions = await GetVersionsAsync(platform, project, game, loader, ct).ConfigureAwait(false);
        return versions.OfType<JsonObject>().Where(x => AllowedChannel(x["version_type"]?.GetValue<string>(), channel)).OrderByDescending(x => x[platform == "modrinth" ? "date_published" : "fileDate"]?.GetValue<string>(), StringComparer.Ordinal).FirstOrDefault()?.DeepClone() as JsonObject ?? throw new InvalidDataException($"No matching versions for {project}; keep the local file or choose a version explicitly.");
    }

    /// <summary>Resolves and installs the selected content dependency group while retaining local tracking and recovery metadata.</summary>
    public async Task<ContentInstallReceipt> InstallAsync(string platform, string projectId, string? versionId, string game, string kind = "mods", string? world = null, IProgress<DownloadProgress>? progress = null, string? minecraftVersion = null, string? loader = null, ContentInstallOptions? options = null, CancellationToken ct = default)
    {
        await _content.RecoverAsync(game, ct).ConfigureAwait(false);
        var plan = await PlanInstallAsync(platform: platform, projectId: projectId, versionId: versionId, game: game, kind: kind, world: world, minecraftVersion: minecraftVersion, loader: loader, ct: ct, options: options).ConfigureAwait(false);
        return await ApplyPlanAsync(plan, progress, ct).ConfigureAwait(false);
    }

    internal async Task<ContentInstallReceipt> ApplyPlanAsync(ContentInstallPlan plan, IProgress<DownloadProgress>? progress, CancellationToken ct,
        Func<IReadOnlyList<ContentPlanNode>, CancellationToken, Task>? validateOnline = null)
    {
        using var cacheUse = await CacheActivity.EnterAsync(_paths, ct).ConfigureAwait(false);
        if (!plan.CanApply)
            throw new LauncherOperationException("CONTENT_PLAN_CONFLICT", "Content plan has unresolved conflicts; existing files were retained. Choose exact versions or manage dependencies manually.", new JsonObject { ["plan"] = JsonSerializer.SerializeToNode(plan, ContentJsonContext.InstallPlan) });
        var candidates = await PreparePlanDownloadsAsync(plan, progress, ct).ConfigureAwait(false);
        foreach (var candidate in candidates)
            await ContentService.InspectSourceAsync(candidate.Kind, candidate.Source!, ct).ConfigureAwait(false);
        var result = await _content.ApplyGroupAsync(plan.GameDirectory, candidates, plan.LockFingerprint, plan.PrimaryProject, ct,
            validateOnline: validateOnline is null ? null : token => validateOnline(candidates, token)).ConfigureAwait(false);
        result.Platform = plan.Platform;
        result.Warnings = plan.Warnings;
        result.Edges = plan.Edges;
        return result;
    }

    internal async Task<IReadOnlyList<ContentPlanNode>> PreparePlanDownloadsAsync(ContentInstallPlan plan, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var candidates = new List<ContentPlanNode>();
        var platform = plan.Platform;
        foreach (var node in plan.Nodes)
        {
            var identity = node.ProjectId + ":" + node.VersionId;
            var cache = Path.Combine(_paths.CacheDirectory, "content-downloads", platform, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
            var local = node.ReuseLocal;
            var file = local is null ? SafeCachePath(cache, node.FileName)
                : Path.GetFullPath(Path.Combine(plan.GameDirectory, local.Path));
            if (local is null)
            {
                await _downloads.DownloadAsync(uri: new Uri(node.DownloadUrl!), destination: file, sha1: node.Sha1, size: node.Size, progress: progress, cancellationToken: ct, headersForUri: platform == "curseforge" ? CurseForge.GetDownloadHeaders : null).ConfigureAwait(false);
            }
            else if (!string.Equals(Path.GetDirectoryName(file),
                ContentService.DirectoryFor(plan.GameDirectory, node.Kind, node.World), StringComparison.OrdinalIgnoreCase)
                || !await DownloadService.ValidAsync(file, node.Sha1, node.Size, ct).ConfigureAwait(false))
            {
                throw new IOException($"The reviewed local content changed; no duplicate was downloaded: {file}");
            }

            if (node.Sha512 is { } sha512)
            {
                string actual;
                {
                    var stream = File.OpenRead(file);
                    await using (stream.ConfigureAwait(false))
                        actual = Convert.ToHexStringLower(await SHA512.HashDataAsync(stream, ct).ConfigureAwait(false));
                }

                if (!string.Equals(actual, sha512, StringComparison.OrdinalIgnoreCase))
                {
                    if (local is not null)
                        throw new IOException($"The reviewed local content changed; the original was retained: {file}");
                    using var lease = await ResourceLease.AcquireAsync(resource: file, cancellationToken: ct).ConfigureAwait(false);
                    if (File.Exists(file))
                        File.Move(file, file + ".invalid-" + Guid.NewGuid().ToString("N"));
                    throw new InvalidDataException($"Content SHA512 mismatch; the invalid cache was quarantined and a retry can download it again: {file}");
                }
            }

            candidates.Add(node with { Source = file });
        }

        return candidates;
    }

    /// <summary>Builds an update plan from recorded sources or local-file discovery, preserving explicit source and compatibility choices.</summary>
    public async Task<ContentResult> PlanUpdateAsync(string game, string kind, string fileName, string? world = null, ContentInstallOptions? options = null, CancellationToken ct = default)
    {
        var item = (await _content.ListAsync(game, kind, world, ct).ConfigureAwait(false)).FirstOrDefault(x => x.FileName == fileName) ?? throw new FileNotFoundException($"Content is missing: {fileName}");
        options ??= new();
        var identified = await IdentifyUpdateSourceAsync(item, options, ct).ConfigureAwait(false);
        return await PlanUpdateCoreAsync(game, kind, fileName, world, options, item, identified, ct).ConfigureAwait(false);
    }

    private async Task<ContentResult> PlanUpdateCoreAsync(string game, string kind, string fileName, string? world,
        ContentInstallOptions options, ContentEntry item, LocalUpdateSource discovery, CancellationToken ct)
    {
        var identified = discovery.Provenance;
        var tracking = UpdateTracking(item, identified);
        if (item.Tracking?["pinned"]?.GetValue<bool>() == true)
            return new ContentUpdateSkipped { Reason = "pinned", FileName = fileName };
        options = options with
        {
            Channel = options.Channel ?? tracking?["channel"]?.GetValue<string>()
        };
        if (tracking?["platform"]?.ToString() is not { Length: > 0 } || tracking["projectId"]?.ToString() is not { Length: > 0 })
            return await PlanSelectedLocalUpdateAsync(game, item, discovery, world, options, ct).ConfigureAwait(false);
        if (tracking["platform"]?.GetValue<string>() == "curseforge")
            _ = CurseForge;
        if (item.Sha1 != tracking["sha1"]?.GetValue<string>())
            throw new IOException("Local content was modified; retain it or choose a replacement explicitly.");
        var gameVersion = options.UseCurrentContext ? options.MinecraftVersion : tracking["gameVersion"]?.GetValue<string>();
        var loader = options.UseCurrentContext ? options.Loader : tracking["loader"]?.GetValue<string>();
        var context = new ContentUpdateContext
        {
            Source = options.UseCurrentContext ? "current-instance" : "recorded-source",
            MinecraftVersion = gameVersion,
            Loader = loader,
            RecordedMinecraftVersion = tracking["gameVersion"]?.GetValue<string>(),
            RecordedLoader = tracking["loader"]?.GetValue<string>(),
            Warning = options.ContextWarning
        };
        ValidateChannel(options.Channel);
        var platform = tracking["platform"]!.GetValue<string>();
        var project = tracking["projectId"]!.GetValue<string>();
        var selected = options.VersionSelections?.GetValueOrDefault(project);
        if (platform == "modrinth" && selected is null && (gameVersion is null || (!options.UseCurrentContext && kind == "mods" && loader is null)))
        {
            return new ContentUpdateSkipped
            {
                Reason = "update-context-required",
                FileName = fileName,
                Warning = "The source is known but game/loader context is missing. Identify with the instance context or select an exact replacement version; the local file remains usable.",
                LocalRetained = true
            };
        }

        JsonObject latest;
        try
        {
            latest = await VersionAsync(project, selected, options.Channel, gameVersion, loader, platform, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound })
        {
            return new ContentUpdateSkipped
            {
                Reason = "upstream-unavailable",
                Warning = ex.Message,
                FileName = fileName,
                LocalRetained = true,
                UpdateContext = context
            };
        }

        if (latest["id"]!.ToString() == tracking["versionId"]?.GetValue<string>())
        {
            return new ContentUpdateSkipped
            {
                Reason = "already-current",
                FileName = fileName,
                UpdateContext = context
            };
        }

        var result = await PlanInstallAsync(platform: platform, projectId: project, versionId: latest["id"]!.ToString(), game: game, kind: kind, world: world, minecraftVersion: gameVersion, loader: loader, ct: ct, options: options).ConfigureAwait(false);
        result.UpdateContext = context;
        if (identified is not null) IncludeIdentifiedLocal(result, project, item);
        return result;
    }

    /// <summary>Applies an update for tracked local content through the shared installation and recovery path.</summary>
    public async Task<ContentResult> UpdateAsync(string game, string kind, string fileName, string? world = null, IProgress<DownloadProgress>? progress = null, ContentInstallOptions? options = null, CancellationToken ct = default)
    {
        await _content.RecoverAsync(game, ct).ConfigureAwait(false);
        var plan = await PlanUpdateAsync(game: game, kind: kind, fileName: fileName, world: world, ct: ct, options: options).ConfigureAwait(false);
        if (plan is ContentUpdateSkipped)
            return plan;
        var installPlan = (ContentInstallPlan)plan;
        var result = await ApplyPlanAsync(installPlan, progress, ct).ConfigureAwait(false);
        result.Updated = true;
        result.UpdateContext = installPlan.UpdateContext;
        return result;
    }

    internal static void ValidateChannel(string? channel)
    {
        if (channel is not null and not ("release" or "beta" or "alpha"))
            throw new ArgumentException("Channel must be release, beta or alpha.");
    }

    internal static bool AllowedChannel(string? type, string? channel) => channel switch
    {
        "release" => type == "release",
        "beta" => type is "release" or "beta",
        _ => true
    };
    /// <summary>Identifies a local content file through the selected provider while preserving unrecognized local files.</summary>
    public Task<ContentIdentityResult> IdentifyAsync(string game, string kind, string fileName, string? world = null, string algorithm = "sha1", bool attach = false, string? minecraftVersion = null, string? loader = null, CancellationToken ct = default)
        => IdentifyExpectedAsync(game: game, kind: kind, fileName: fileName, expectedProjectId: null, expectedVersionId: null, world: world, algorithm: algorithm, attach: attach, ct: ct, minecraftVersion: minecraftVersion, loader: loader);

    /// <summary>Identifies a file while checking the requested project/version before changing its provenance.</summary>
    public async Task<ContentIdentityResult> IdentifyExpectedAsync(string game, string kind, string fileName, string? expectedProjectId, string? expectedVersionId, string? world = null, string algorithm = "sha1", bool attach = false, string? minecraftVersion = null, string? loader = null, CancellationToken ct = default)
    {
        if (algorithm is not ("sha1" or "sha512"))
            throw new ArgumentException("Hash algorithm must be sha1 or sha512.");
        var item = (await _content.ListAsync(game, kind, world, ct).ConfigureAwait(false)).FirstOrDefault(x => x.FileName == fileName) ?? throw new FileNotFoundException($"Content is missing: {fileName}");
        if (item.Directory)
        {
            return new ContentIdentityResult
            {
                Matched = false,
                Reason = "directory-content",
                UsableLocally = true
            };
        }

        var path = Path.Combine(ContentService.DirectoryFor(game, kind, world), fileName);
        var stream = File.OpenRead(path);
        await using var streamDisposal = stream.ConfigureAwait(false);
        var hash = algorithm == "sha1" ? item.Sha1! : Convert.ToHexStringLower(await SHA512.HashDataAsync(stream, ct).ConfigureAwait(false));
        var result = await LookupIdentityAsync(hash, algorithm, expectedProjectId, expectedVersionId,
            item.Tracking?["gameVersion"]?.GetValue<string>() ?? minecraftVersion,
            item.Tracking?["loader"]?.GetValue<string>() ?? loader, attach, ct).ConfigureAwait(false);
        if (attach && result.Provenance is { } provenance)
            result.Content = await _content.AttachProvenanceAsync(game, kind, fileName, world, provenance, item.Sha1!, ct).ConfigureAwait(false);
        return result;
    }

    private async Task<ContentIdentityResult> LookupIdentityAsync(string hash, string algorithm, string? expectedProjectId,
        string? expectedVersionId, string? minecraftVersion, string? loader, bool attach, CancellationToken ct)
    {
        JsonObject version;
        try
        {
            version = (await ApiAsync("version_file/" + hash + "?algorithm=" + algorithm, ct).ConfigureAwait(false)).AsObject();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new ContentIdentityResult
            {
                Matched = false,
                Hash = hash,
                Algorithm = algorithm,
                UsableLocally = true
            };
        }

        var matching = ((version["files"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(x => string.Equals(x["hashes"]?[algorithm]?.GetValue<string>(), hash, StringComparison.OrdinalIgnoreCase))) ?? throw new InvalidDataException("The hash lookup returned a version without a matching file; provenance was not attached.");
        if ((expectedProjectId is not null && version["project_id"]?.ToString() != expectedProjectId)
            || (expectedVersionId is not null && version["id"]?.ToString() != expectedVersionId))
        {
            throw new LauncherOperationException("CONTENT_IDENTITY_CHANGED", "The file's identified source differs from the selected project or version; no provenance was attached.",
                new JsonObject
                {
                    ["expectedProjectId"] = expectedProjectId,
                    ["expectedVersionId"] = expectedVersionId,
                    ["actualProjectId"] = version["project_id"]?.DeepClone(),
                    ["actualVersionId"] = version["id"]?.DeepClone()
                });
        }

        var provenance = new ContentProvenance
        {
            Platform = "modrinth",
            ProjectId = version["project_id"]!.GetValue<string>(),
            VersionId = version["id"]!.GetValue<string>(),
            DownloadUrl = matching["url"]?.GetValue<string>(),
            Upstream = (JsonObject)version.DeepClone(),
            GameVersion = minecraftVersion,
            Loader = loader
        };
        var result = new ContentIdentityResult
        {
            Matched = true,
            Hash = hash,
            Algorithm = algorithm,
            Attached = attach,
            Provenance = provenance
        };
        if (minecraftVersion is null || loader is null)
            result.Warning = "No complete instance context was supplied; source matching does not infer which game/loader version you intend to use.";
        return result;
    }
}