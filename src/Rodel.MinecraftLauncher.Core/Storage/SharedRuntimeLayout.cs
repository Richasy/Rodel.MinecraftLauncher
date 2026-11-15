// Copyright (c) Richasy. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Storage;

/// <summary>Routes managed runtime files to shared dependencies; unmarked legacy roots retain their layout.</summary>
internal static class SharedRuntimeLayout
{
    internal const string FileName = "dependencies.json";

    internal static async Task InitializeAsync(LauncherPaths paths, string root, CancellationToken ct)
    {
        var references = new JsonObject { ["schemaVersion"] = 1, ["private"] = true, ["clients"] = new JsonObject() };
        foreach (var kind in new[] { "assets", "libraries" })
        {
            var id = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.Combine(paths.DependenciesDirectory, kind, id));
            references[kind] = id;
        }

        await JsonFiles.WriteAsync(Path.Combine(root, FileName), references, ct).ConfigureAwait(false);
    }

    // Keep the current references valid throughout publication. Only private working sets
    // may be discarded after the atomic reference switch; shared sets remain immutable.
    internal static async Task SealAsync(LauncherPaths paths, string root, CancellationToken ct)
    {
        var file = Path.Combine(root, FileName);
        if (!File.Exists(file)) return;
        var references = JsonFiles.ReadObject(file);
        if (references["private"]?.GetValue<bool>() != true) return;
        var previous = new List<string>();
        foreach (var kind in new[] { "assets", "libraries" })
        {
            var source = Resolve(root, kind);
            var hash = await TreeHashAsync(source, ct).ConfigureAwait(false);
            var target = Path.Combine(paths.DependenciesDirectory, kind, hash);
            using var lease = await ResourceLease.AcquireAsync(resource: target, cancellationToken: ct).ConfigureAwait(false);
            if (!Directory.Exists(target))
            {
                var stage = target + "." + Guid.NewGuid().ToString("N") + ".partial";
                try
                {
                    Directory.CreateDirectory(stage);
                    foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                        Directory.CreateDirectory(Path.Combine(stage, Path.GetRelativePath(source, directory)));
                    foreach (var entry in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                    {
                        using var input = File.OpenRead(entry);
                        using var output = File.Create(Path.Combine(stage, Path.GetRelativePath(source, entry)));
                        await input.CopyToAsync(output, ct).ConfigureAwait(false);
                    }

                    await PublishDirectoryAsync(stage, target, hash, ct).ConfigureAwait(false);
                }
                finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            }
            else if (await TreeHashAsync(target, ct).ConfigureAwait(false) != hash)
            {
                throw new IOException("A shared dependency set is damaged; the private installation is retained.");
            }

            references[kind] = hash;
            if (!source.Equals(target, StringComparison.OrdinalIgnoreCase)) previous.Add(source);
        }

        if (references["clients"] is JsonObject clients)
        {
            foreach (var version in clients.Select(p => p.Key).ToArray())
            {
                var source = Resolve(root, "versions/" + version + "/" + version + ".jar");
                if (!File.Exists(source)) continue;
                var hash = await FileHashAsync(source, ct).ConfigureAwait(false);
                var target = Path.Combine(paths.DependenciesDirectory, "clients", hash + ".jar");
                using var lease = await ResourceLease.AcquireAsync(resource: target, cancellationToken: ct).ConfigureAwait(false);
                if (!File.Exists(target))
                {
                    var stage = target + "." + Guid.NewGuid().ToString("N") + ".partial";
                    try
                    {
                        using (var input = File.OpenRead(source))
                        using (var output = File.Create(stage))
                            await input.CopyToAsync(output, ct).ConfigureAwait(false);
                        File.Move(stage, target);
                    }
                    finally { if (File.Exists(stage)) File.Delete(stage); }
                }
                else if (await FileHashAsync(target, ct).ConfigureAwait(false) != hash)
                {
                    throw new IOException("A shared client is damaged; the private installation is retained.");
                }

                clients[version] = hash;
                if (!source.Equals(target, StringComparison.OrdinalIgnoreCase)) previous.Add(source);
            }
        }

        references.Remove("private");
        await JsonFiles.WriteAsync(file, references, ct).ConfigureAwait(false);
        foreach (var path in previous)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    // Import stages remain private until their complete content has been identified.
    // Published dependencies are retained as a cache if a later registration is cancelled.
    internal static async Task<SharedRuntimeReferences> PublishAsync(LauncherPaths paths, string runtime, string java, JsonObject settings, CancellationToken ct)
    {
        var references = new SharedRuntimeReferences();
        foreach (var kind in new[] { "assets", "libraries" })
        {
            var source = Path.Combine(runtime, kind);
            Directory.CreateDirectory(source);
            var hash = await TreeHashAsync(source, ct).ConfigureAwait(false);
            var target = Path.Combine(paths.DependenciesDirectory, kind, hash);
            await PublishDirectoryAsync(source, target, hash, ct).ConfigureAwait(false);
            Rebase(settings, source, target);
            if (kind == "assets") references.Assets = hash;
            else references.Libraries = hash;
        }

        var versions = Path.Combine(runtime, "versions");
        if (Directory.Exists(versions))
        {
            foreach (var directory in Directory.EnumerateDirectories(versions))
            {
                var version = Path.GetFileName(directory);
                var jar = Path.Combine(directory, version + ".jar");
                if (!File.Exists(jar)) continue;
                var hash = await FileHashAsync(jar, ct).ConfigureAwait(false);
                var target = Path.Combine(paths.DependenciesDirectory, "clients", hash + ".jar");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    if (await FileHashAsync(target, ct).ConfigureAwait(false) != hash)
                        throw new IOException("A shared client archive is damaged; repair it before importing.");
                    File.Delete(jar);
                }
                else
                {
                    File.Move(jar, target);
                }

                references.Clients[version] = hash;
            }
        }

        if (Directory.Exists(java))
        {
            var hash = await TreeHashAsync(java, ct).ConfigureAwait(false);
            var id = "import-" + hash;
            var directory = Path.Combine(paths.RuntimesDirectory, id);
            var target = Path.Combine(directory, "runtime");
            var selected = settings["javaPath"]!.GetValue<string>();
            var relative = Path.GetRelativePath(java, selected);
            await PublishDirectoryAsync(java, target, hash, ct).ConfigureAwait(false);
            Rebase(settings, java, target);
            await JsonFiles.WriteAsync(Path.Combine(directory, "registration.json"), new JsonObject
            {
                ["id"] = id,
                ["managed"] = true,
                ["source"] = "instance-import",
                ["contentSha256"] = hash,
                ["runtimeDirectory"] = "runtime",
                ["javaPath"] = Path.Combine("runtime", relative)
            }, ct).ConfigureAwait(false);
            settings.Remove("javaPath");
            settings["javaRuntimeId"] = id;
            settings["javaRuntimeExecutable"] = relative;
        }

        return references;
    }

    private static void Rebase(JsonNode node, string source, string target)
    {
        string Replace(string value) => value.Replace(source, target, StringComparison.OrdinalIgnoreCase)
            .Replace(source.Replace('\\', '/'), target.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToArray())
            {
                if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text)) obj[key] = Replace(text);
                else if (obj[key] is { } child) Rebase(child, source, target);
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text)) array[i] = Replace(text);
                else if (array[i] is { } child) Rebase(child, source, target);
            }
        }
    }

    private static async Task PublishDirectoryAsync(string source, string target, string hash, CancellationToken ct)
    {
        if (Directory.Exists(target))
        {
            if (await TreeHashAsync(target, ct).ConfigureAwait(false) != hash)
                throw new IOException("A shared dependency set is damaged; repair it before importing.");
            Directory.Delete(source, true);
        }
        else
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            for (var attempt = 0; ; attempt++)
            {
                try { Directory.Move(source, target); break; }
                catch (IOException ex) when (attempt < 5 && (ex.HResult & 0xffff) is 5 or 32 or 33)
                {
                    await Task.Delay(100, ct).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task<string> FileHashAsync(string file, CancellationToken ct)
    {
        using var input = File.OpenRead(file);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(input, ct).ConfigureAwait(false));
    }

    private static async Task<string> TreeHashAsync(string directory, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories).OrderBy(p => Path.GetRelativePath(directory, p), StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(directory, entry).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relative + "\0"));
            hash.AppendData(Encoding.UTF8.GetBytes(Directory.Exists(entry) ? "directory\0" : await FileHashAsync(entry, ct).ConfigureAwait(false) + "\0"));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // Existing installation APIs may write dependencies. Fork once before they do so,
    // keeping the referenced immutable set available to every other instance.
    internal static async Task EnsureWritableAsync(LauncherPaths paths, string root, CancellationToken ct)
    {
        var file = Path.Combine(root, FileName);
        if (!File.Exists(file)) return;
        var metadata = JsonFiles.ReadObject(file);
        if (metadata["private"]?.GetValue<bool>() == true) return;
        var created = new List<string>();
        try
        {
            foreach (var kind in new[] { "assets", "libraries" })
            {
                var source = Resolve(root, kind);
                var id = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                var target = Path.Combine(paths.DependenciesDirectory, kind, id);
                created.Add(target);
                Directory.CreateDirectory(target);
                foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                    Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
                foreach (var entry in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    using var input = File.OpenRead(entry);
                    using var output = File.Create(Path.Combine(target, Path.GetRelativePath(source, entry)));
                    await input.CopyToAsync(output, ct).ConfigureAwait(false);
                }

                metadata[kind] = id;
            }

            if (metadata["clients"] is JsonObject clients)
            {
                foreach (var version in clients.Select(p => p.Key).ToArray())
                {
                    var source = Resolve(root, "versions/" + version + "/" + version + ".jar");
                    var id = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                    var target = Path.Combine(paths.DependenciesDirectory, "clients", id + ".jar");
                    clients[version] = id;
                    if (!File.Exists(source)) continue; // A prior interrupted installation can leave an unfilled slot.
                    created.Add(target);
                    using var input = File.OpenRead(source);
                    using var output = File.Create(target);
                    await input.CopyToAsync(output, ct).ConfigureAwait(false);
                }
            }

            metadata["private"] = true;
            await JsonFiles.WriteAsync(file, metadata, ct).ConfigureAwait(false);
        }
        catch
        {
            foreach (var path in created)
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
            }

            throw;
        }
    }

    internal static async Task EnsureClientSlotAsync(LauncherPaths paths, string root, string version, CancellationToken ct)
    {
        var file = Path.Combine(root, FileName);
        if (!File.Exists(file)) return;
        var metadata = JsonFiles.ReadObject(file);
        var clients = metadata["clients"]!.AsObject();
        if (clients[version] is not null) return;
        clients[version] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(paths.DependenciesDirectory, "clients"));
        await JsonFiles.WriteAsync(file, metadata, ct).ConfigureAwait(false);
    }
    internal static (string Root, string Relative) Route(string root, string relative)
    {
        root = Path.GetFullPath(root);
        var parts = relative.Replace('\\', '/').Split('/');
        var file = Path.Combine(root, FileName);
        if (!File.Exists(file)) return (root, relative);
        if (parts.Any(p => p is "." or "..") || Path.IsPathFullyQualified(relative))
            throw new InvalidDataException("Runtime dependency paths must stay within their storage root.");
        var metadata = JsonFiles.ReadObject(file);
        if (metadata["schemaVersion"]?.GetValue<int>() != 1)
            throw new InvalidDataException("Unsupported runtime dependency layout.");
        var instance = Directory.GetParent(root);
        if (instance?.Parent?.Name != "instances")
            throw new InvalidDataException("Shared runtime metadata must belong to a managed instance.");
        var dependencies = Path.Combine(instance.Parent.Parent!.FullName, "dependencies");
        static string Id(string value)
        {
            if (value.Length != 64 || !value.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid shared dependency identifier.");
            return value;
        }

        if (parts[0] is "assets" or "libraries" && metadata[parts[0]]?.GetValue<string>() is { } set)
            return (Path.Combine(dependencies, parts[0], Id(set)), string.Join('/', parts.Skip(1)));
        if (parts.Length == 3 && parts[0] == "versions" && parts[2] == parts[1] + ".jar"
            && metadata["clients"]?[parts[1]]?.GetValue<string>() is { } client)
        {
            return (Path.Combine(dependencies, "clients"), Id(client) + ".jar");
        }

        return (root, relative);
    }
    internal static string Resolve(string root, string relative)
    {
        var (Root, Relative) = Route(root, relative);
        var path = Path.GetFullPath(Path.Combine(Root, Relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.Equals(Root, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Runtime dependency path escapes its storage root.");
        return path;
    }
}