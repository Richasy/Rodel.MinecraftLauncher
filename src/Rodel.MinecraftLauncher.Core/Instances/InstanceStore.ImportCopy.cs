// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Instances;

public sealed partial class InstanceStore
{
    private sealed record ImportFile(string Source, string Target, long Length, DateTime Modified);

    private async Task<InstanceRecord> CopyImportAsync(string name, InstanceCopySource preview, IProgress<LauncherProgress>? progress, CancellationToken token, JsonObject? template = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var sourceGame = preview.GameDirectory;
        var sourceRuntime = preview.MinecraftDirectory;
        var version = preview.VersionId;
        var id = Guid.NewGuid().ToString("N");
        var targetRoot = Path.Combine(paths.InstancesDirectory, id);
        var game = Path.Combine(targetRoot, "game");
        var runtime = Path.Combine(targetRoot, "runtime");
        using var lease = await ResourceLease.AcquireAsync(resource: MetadataPath(id), cancellationToken: token).ConfigureAwait(false);
        var files = new List<ImportFile>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rebases = new List<(string Source, string Target)> { (sourceGame, game), (sourceRuntime, runtime) };
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var settings = preview.Settings.DeepClone().AsObject();
        void Report(string stage, long bytes = 0, long? total = null)
        {
            try
            {
                progress?.Report(new LauncherProgress { Stage = stage, Bytes = bytes, TotalBytes = total, Files = files.Count });
            }
            catch (Exception exception) { System.Diagnostics.Trace.TraceWarning("Instance copy progress observer failed: {0}", exception.Message); }
        }

        void AddFile(string from, string to)
        {
            token.ThrowIfCancellationRequested();
            if (!targets.Add(to))
                return;
            var info = new FileInfo(from);
            files.Add(new(from, to, info.Length, info.LastWriteTimeUtc));
        }

        void Plan(string from, string to, bool gameRoot = false)
        {
            token.ThrowIfCancellationRequested();
            directories.Add(to);
            var physical = PhysicalPath(from);
            var destination = PhysicalPath(targetRoot);
            if (SamePath(destination, physical) || IsInside(destination, physical))
                throw new IOException("Import destination must not be inside a source directory.");
            if (!active.Add(physical))
                throw new IOException("Cannot import a directory link cycle.");
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(from))
                {
                    token.ThrowIfCancellationRequested();
                    var leaf = Path.GetFileName(entry);
                    if (gameRoot && SamePath(sourceGame, sourceRuntime) && (leaf is "assets" or "libraries" or "versions"))
                        continue;
                    if (gameRoot && version is not null && SamePath(sourceGame, Path.Combine(sourceRuntime, "versions", version))
                        && (leaf == version + ".json" || leaf == version + ".jar"))
                    {
                        continue;
                    }

                    if (gameRoot && (leaf is "launcher_accounts.json" or "accounts.json" or "user-accounts.json" || leaf == OwnerFile))
                        continue;
                    var target = Path.Combine(to, leaf);
                    if (Directory.Exists(entry))
                        Plan(entry, target);
                    else
                        AddFile(entry, target);
                }
            }
            finally { active.Remove(physical); }
        }

        Report("import-scanning");
        Plan(sourceGame, game, true);
        foreach (var directory in new[] { "assets", "libraries" })
        {
            var from = SharedRuntimeLayout.Resolve(sourceRuntime, directory);
            if (Directory.Exists(from))
                Plan(from, Path.Combine(runtime, directory));
        }

        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = version;
        while (!string.IsNullOrWhiteSpace(current))
        {
            token.ThrowIfCancellationRequested();
            if (current != Path.GetFileName(current) || current is "." or ".." || !versions.Add(current))
                throw new InvalidDataException("Invalid or cyclic version inheritance in imported metadata.");
            var directory = Path.Combine(sourceRuntime, "versions", current);
            var json = Path.Combine(directory, current + ".json");
            if (!File.Exists(json))
                break;
            foreach (var file in Directory.EnumerateFiles(directory))
                AddFile(file, Path.Combine(runtime, "versions", current, Path.GetFileName(file)));
            var client = SharedRuntimeLayout.Resolve(sourceRuntime, "versions/" + current + "/" + current + ".jar");
            if (File.Exists(client))
                AddFile(client, Path.Combine(runtime, "versions", current, current + ".jar"));
            current = JsonFiles.ReadObject(json)["inheritsFrom"]?.GetValue<string>();
        }

        var configuredJava = settings["javaPath"]?.GetValue<string>();
        var java = string.IsNullOrWhiteSpace(configuredJava) ? null : Java.JavaDiscovery.ResolveExecutable(configuredJava, sourceGame);
        if (java is not null && File.Exists(java))
        {
            var bin = Path.GetDirectoryName(java)!;
            if (Path.GetFileName(bin).Equals("bin", StringComparison.OrdinalIgnoreCase))
            {
                var javaRoot = Directory.GetParent(bin)!.FullName;
                var targetJava = Path.Combine(targetRoot, "java");
                Plan(javaRoot, targetJava);
                rebases.Add((javaRoot, targetJava));
                settings["javaPath"] = Path.Combine(targetJava, "bin", Path.GetFileName(java));
            }
        }

        string RebaseImportedPath(string text)
        {
            var mappings = rebases.SelectMany(pair => new[]
            {
                (Source: Path.TrimEndingDirectorySeparator(pair.Source), Target: Path.TrimEndingDirectorySeparator(pair.Target)),
                (Source: Path.TrimEndingDirectorySeparator(pair.Source).Replace('\\', '/'), Target: Path.TrimEndingDirectorySeparator(pair.Target).Replace('\\', '/')),
            }).Distinct().OrderByDescending(pair => pair.Source.Length).ToArray();
            var result = new System.Text.StringBuilder();
            var cursor = 0;
            while (cursor < text.Length)
            {
                var bestIndex = -1;
                var best = (Source: string.Empty, Target: string.Empty);
                foreach (var pair in mappings)
                {
                    var index = text.IndexOf(pair.Source, cursor, StringComparison.OrdinalIgnoreCase);
                    while (index >= 0)
                    {
                        var end = index + pair.Source.Length;
                        var startsPath = index == 0 || text[index - 1] is '=' or ';' or ':' or '"' or '\'' or '/' || char.IsWhiteSpace(text[index - 1]);
                        var endsPath = end == text.Length || text[end] is '\\' or '/' or ';' or '"' or '\'';
                        if (startsPath && endsPath) break;
                        index = text.IndexOf(pair.Source, index + 1, StringComparison.OrdinalIgnoreCase);
                    }

                    if (index >= 0 && (bestIndex < 0 || index < bestIndex))
                    {
                        bestIndex = index;
                        best = pair;
                    }
                }

                if (bestIndex < 0) break;
                result.Append(text, cursor, bestIndex - cursor).Append(best.Target);
                cursor = bestIndex + best.Source.Length;
            }

            return result.Append(text, cursor, text.Length - cursor).ToString();
        }

        void RebaseSettings(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                foreach (var key in obj.Select(p => p.Key).ToArray())
                {
                    if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        obj[key] = RebaseImportedPath(text);
                    }
                    else if (obj[key] is { } child)
                    {
                        RebaseSettings(child);
                    }
                }
            }
            else if (node is JsonArray array)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        array[i] = RebaseImportedPath(text);
                    }
                    else if (array[i] is { } child)
                    {
                        RebaseSettings(child);
                    }
                }
            }
        }

        RebaseSettings(settings);
        var total = files.Sum(f => f.Length);
        long copied = 0;
        try
        {
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(runtime);
            foreach (var directory in directories) Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(game, OwnerFile), id, token).ConfigureAwait(false);
            Report("import-copy", copied, total);
            var buffer = new byte[131072];
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
                using var input = new FileStream(file.Source, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length);
                using var output = new FileStream(file.Target, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length);
                int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    copied += read;
                    Report("import-copy", copied, total);
                }

                await output.FlushAsync(token).ConfigureAwait(false);
                output.Flush(true);
                if (input.Length != file.Length || File.GetLastWriteTimeUtc(file.Source) != file.Modified)
                    throw new IOException("A source file changed during import. Retry after closing the source game.");
            }

            token.ThrowIfCancellationRequested();
            Report("import-register", copied, total);
            using var dependencyLease = await ResourceLease.AcquireAsync(resource: paths.DependenciesDirectory, cancellationToken: token).ConfigureAwait(false);
            var references = await SharedRuntimeLayout.PublishAsync(paths, runtime, Path.Combine(targetRoot, "java"), settings, token).ConfigureAwait(false);
            await JsonFiles.WriteAsync(Path.Combine(runtime, SharedRuntimeLayout.FileName), JsonSerializer.SerializeToNode(references, StorageJsonContext.Default.SharedRuntimeReferences)!, token).ConfigureAwait(false);
            var metadata = new JsonObject
            {
                ["schemaVersion"] = 2,
                ["id"] = id,
                ["name"] = name,
                ["versionId"] = version,
                ["gameDirectory"] = "game",
                ["minecraftDirectory"] = "runtime",
                ["managed"] = true,
                ["settings"] = settings,
                ["sourceSettings"] = preview.SourceSettings?.DeepClone()
            };
            if (template is not null)
            {
                foreach (var pair in template)
                    if (pair.Key != "javaPath" && !metadata.ContainsKey(pair.Key)) metadata[pair.Key] = pair.Value?.DeepClone();
            }

            var instance = await SaveAsync(id, metadata, token).ConfigureAwait(false);
            Report("import-complete", copied, total);
            return instance;
        }
        catch
        {
            // Only this newly generated destination is owned; the source is never a cleanup target.
            DeleteNewDirectory(targetRoot);
            throw;
        }
    }
}