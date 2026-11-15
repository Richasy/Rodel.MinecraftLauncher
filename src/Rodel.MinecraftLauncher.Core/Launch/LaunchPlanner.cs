// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Creates a read-only launch description. File preparation and process lifetime belong to callers.</summary>
public sealed class LaunchPlanner(LauncherPaths? paths = null)
{
    /// <summary>Resolves launch metadata, arguments, memory, and runtime settings into a plan without creating a game process.</summary>
    public async Task<LaunchPlan> BuildAsync(string minecraftDirectory, string gameDirectory, string versionId, string? javaPath = null, string playerName = "Player", int maxMemoryMb = 2048, IEnumerable<string>? extraJvmArguments = null, int? minMemoryMb = null, LaunchResolution? launchOptions = null, IdentityRecord? identity = null, CancellationToken ct = default)
    {
        minecraftDirectory = Path.GetFullPath(minecraftDirectory);
        gameDirectory = Path.GetFullPath(gameDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMemoryMb);
        if (minMemoryMb is <= 0)
            throw new ArgumentOutOfRangeException(nameof(minMemoryMb));
        if (string.IsNullOrWhiteSpace(playerName))
            throw new ArgumentException("An offline player name is required.", nameof(playerName));
        var version = await new VersionResolver().ResolveAsync(minecraftDirectory, versionId, ct).ConfigureAwait(false);
        var warnings = new List<string>();
        var required = new Dictionary<string, LaunchRequiredFile>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, string kind, JsonNode? metadata = null, string? url = null, string[]? excludes = null)
        {
            path = Path.GetFullPath(path);
            required[path] = new(path, kind, Text(metadata, "url") ?? url, Text(metadata, "sha1"), excludes);
        }

        var recommendedJava = JavaSelection.PreferredMajor(version);
        var discovery = new JavaDiscovery(paths);
        JavaInstallation[] candidates;
        JavaInstallation? java;
        if (javaPath is not null)
        {
            var explicitJava = JavaDiscovery.ResolveExecutable(javaPath, gameDirectory);
            java = await discovery.ProbeAsync(explicitJava, ct).ConfigureAwait(false);
            candidates = [java];
            if (!java.IsUsable)
                warnings.Add($"Explicit Java probe failed: {java.VersionOutput}. The selected path is retained.");
        }
        else
        {
            candidates = await discovery.DiscoverAsync(minecraftDirectory: minecraftDirectory, ct: ct).ConfigureAwait(false);
            java = JavaSelection.Choose(candidates, recommendedJava, version);
            if (java is null)
                warnings.Add("No usable Java was discovered. Supply a Java executable before launching.");
        }

        var selectionReport = JavaSelection.Describe(candidates, java, version, javaPath is not null);
        warnings.AddRange(selectionReport.Warnings);
        if (java is not null && recommendedJava is not null && java.MajorVersion != recommendedJava)
            warnings.Add($"Version recommends Java {recommendedJava}; selected Java reports {java.MajorVersion?.ToString() ?? "an unknown version"}.");
        if (java is not null && JavaSelection.CompatibilityNote(version, java) is { } note)
            warnings.Add(note);
        var architectureRules = (version["libraries"] as JsonArray ?? []).Select(item => item?["rules"])
            .Concat((version["arguments"]?["jvm"] as JsonArray ?? []).OfType<JsonObject>().Select(item => item["rules"]))
            .Concat((version["arguments"]?["game"] as JsonArray ?? []).OfType<JsonObject>().Select(item => item["rules"]))
            .Any(MetadataRuleReader.HasArchitectureRestriction);
        var nativeArchTemplate = (version["libraries"] as JsonArray ?? []).Any(item => item?["natives"]?["windows"]?.GetValue<string>()?.Contains("${arch}", StringComparison.Ordinal) == true);
        if (java is { IsUsable: true } && ((java.Architecture is null && architectureRules) || (java.Bitness is null && nativeArchTemplate)))
            warnings.Add("Selected Java did not report all architecture properties; architecture-dependent metadata uses OS defaults for missing values.");
        var natives = Path.Combine(gameDirectory, ".rodel", "natives", versionId);
        var classpath = new List<string>();
        var features = new Dictionary<string, bool>
        {
            ["is_demo_user"] = false,
            ["has_custom_resolution"] = launchOptions?.Width is not null || launchOptions?.Height is not null,
            ["has_quick_plays_support"] = false,
            ["is_quick_play_singleplayer"] = false,
            ["is_quick_play_multiplayer"] = false,
            ["is_quick_play_realms"] = false
        };
        foreach (var library in (version["libraries"] as JsonArray ?? []).OfType<JsonObject>())
        {
            ct.ThrowIfCancellationRequested();
            if (!MetadataRules.Allows(library["rules"], features, java?.Architecture))
                continue;
            var artifact = library["downloads"]?["artifact"];
            var name = Text(library, "name");
            var artifactPath = Text(artifact, "path");
            if (artifactPath is null && name is not null && (library["downloads"] is null || artifact is not null))
                artifactPath = MetadataRules.MavenPath(name);
            if (artifactPath is not null)
            {
                var fullPath = SafeCombine(SharedRuntimeLayout.Resolve(minecraftDirectory, "libraries"), artifactPath);
                Add(fullPath, "library", artifact ?? library, LibraryUrl(library, artifactPath));
                classpath.Add(fullPath);
            }

            var classifier = library["natives"]?["windows"]?.GetValue<string>().Replace("${arch}", (java?.Bitness ?? (Environment.Is64BitOperatingSystem ? 64 : 32)).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
            if (classifier is not null)
            {
                var nativeArtifact = library["downloads"]?["classifiers"]?[classifier];
                var nativePath = Text(nativeArtifact, "path") ?? (name is null ? null : MetadataRules.MavenPath(name + ":" + classifier));
                if (nativePath is not null)
                    Add(SafeCombine(SharedRuntimeLayout.Resolve(minecraftDirectory, "libraries"), nativePath), "native", nativeArtifact, LibraryUrl(library, nativePath), (library["extract"]?["exclude"] as JsonArray)?.Select(x => x!.GetValue<string>()).ToArray());
                else
                    warnings.Add($"Native classifier '{classifier}' has no file path.");
            }
        }

        var jarVersion = Text(version, "_clientJarVersion") ?? versionId;
        var clientJar = SharedRuntimeLayout.Resolve(minecraftDirectory, "versions/" + jarVersion + "/" + jarVersion + ".jar");
        var ownerJson = Path.Combine(minecraftDirectory, "versions", jarVersion, jarVersion + ".json");
        var clientDescriptor = File.Exists(ownerJson) ? JsonFiles.ReadObject(ownerJson)["downloads"]?["client"] : null;
        Add(clientJar, "client", clientDescriptor);
        classpath.Add(clientJar);
        var assetsRoot = SharedRuntimeLayout.Resolve(minecraftDirectory, "assets");
        var assetIndex = Text(version["assetIndex"], "id") ?? Text(version, "assets") ?? "legacy";
        var indexPath = SafeCombine(Path.Combine(assetsRoot, "indexes"), assetIndex + ".json");
        Add(indexPath, "assetIndex", version["assetIndex"]);
        var gameAssets = File.Exists(Path.Combine(minecraftDirectory, SharedRuntimeLayout.FileName))
            ? Path.Combine(gameDirectory, ".rodel", "assets", assetIndex)
            : Path.Combine(assetsRoot, "virtual", assetIndex);
        if (File.Exists(indexPath))
        {
            var stream = File.OpenRead(indexPath);
            await using var streamDisposal = stream.ConfigureAwait(false);
            var index = await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var virtualAssets = index?["virtual"]?.GetValue<bool>() ?? false;
            var mapped = index?["map_to_resources"]?.GetValue<bool>() ?? false;
            if (!virtualAssets && !mapped)
                gameAssets = assetsRoot;
            if (mapped)
                gameAssets = Path.Combine(gameDirectory, "resources");
            foreach (var (logicalName, item) in index?["objects"] as JsonObject ?? [])
            {
                var hash = Text(item, "hash");
                if (hash is null || hash.Length != 40 || !hash.All(Uri.IsHexDigit))
                {
                    warnings.Add($"Asset '{logicalName}' has an invalid content hash.");
                    continue;
                }

                var assetPath = Path.Combine(assetsRoot, "objects", hash[..2], hash);
                required[assetPath] = new(assetPath, "asset", $"https://resources.download.minecraft.net/{hash[..2]}/{hash}", hash);
                if (virtualAssets || mapped)
                    required[SafeCombine(gameAssets, logicalName)] = new(SafeCombine(gameAssets, logicalName), "assetMapped", null, hash);
            }
        }

        var external = identity?.Type == "external";
        var online = identity?.Type == "microsoft" || external;
        var uuid = online ? identity!.Uuid ?? throw new InvalidDataException("The selected authenticated account has no player identifier.") : OfflineUuid(playerName);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = external ? "__RODEL_EXTERNAL_PLAYER__" : playerName,
            ["version_name"] = versionId,
            ["game_directory"] = gameDirectory,
            ["assets_root"] = assetsRoot,
            ["assets_index_name"] = assetIndex,
            ["auth_uuid"] = uuid,
            ["auth_access_token"] = online ? "__RODEL_MINECRAFT_TOKEN__" : "0",
            ["auth_session"] = external ? "__RODEL_MINECRAFT_TOKEN__" : online ? "token:__RODEL_MINECRAFT_TOKEN__:" + uuid : "0",
            ["user_type"] = external ? "mojang" : online ? "msa" : "legacy",
            ["version_type"] = Text(version, "type") ?? "release",
            ["user_properties"] = "{}",
            ["natives_directory"] = natives,
            ["launcher_name"] = "Rodel.MinecraftLauncher",
            ["launcher_version"] = "0.1.0",
            ["classpath"] = string.Join(Path.PathSeparator, classpath.Distinct(StringComparer.OrdinalIgnoreCase)),
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = SharedRuntimeLayout.Resolve(minecraftDirectory, "libraries"),
            ["libraries_directory"] = SharedRuntimeLayout.Resolve(minecraftDirectory, "libraries"),
            ["file_separator"] = Path.DirectorySeparatorChar.ToString(),
            ["primary_jar"] = clientJar,
            ["primary_jar_name"] = Path.GetFileName(clientJar),
            ["resolution_width"] = launchOptions?.Width?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "1280",
            ["resolution_height"] = launchOptions?.Height?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "720",
            ["game_assets"] = gameAssets,
            ["auth_xuid"] = identity?.Type == "microsoft" ? "__RODEL_XUID__" : string.Empty,
            ["clientid"] = identity?.Type == "microsoft" ? "__RODEL_CLIENT_ID__" : string.Empty
        };
        string Expand(string value) => Regex.Replace(value, @"\$\{([^}]+)\}", match =>
        {
            if (values.TryGetValue(match.Groups[1].Value, out var replacement))
                return replacement;
            warnings.Add($"Unresolved argument placeholder: {match.Value}");
            return match.Value;
        }, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var arguments = new List<string>
        {
            $"-Xmx{maxMemoryMb}M"};
        if (minMemoryMb is { } initialMemory)
        {
            arguments.Add($"-Xms{Math.Min(initialMemory, maxMemoryMb)}M");
            if (initialMemory > maxMemoryMb)
                warnings.Add("Initial memory was reduced to the maximum memory limit.");
        }

        var jvm = ReadArguments(version["arguments"]?["jvm"], features, java?.Architecture).Select(Expand).Select(argument =>
        {
            // BootstrapLauncher scans the classpath for modules. An inherited vanilla jar must
            // be ignored under its real filename, which can differ from ${version_name}.jar.
            const string prefix = "-DignoreList=";
            if (!argument.StartsWith(prefix, StringComparison.Ordinal))
                return argument;
            var actualJarName = Path.GetFileName(clientJar);
            var names = argument[prefix.Length..].Split(',');
            return names.Contains(actualJarName, StringComparer.Ordinal) ? argument : argument + "," + actualJarName;
        }).ToArray();
        arguments.AddRange(jvm);
        if (!jvm.Any(x => x.StartsWith("-Djava.library.path=", StringComparison.Ordinal)))
            arguments.Add("-Djava.library.path=" + natives);
        if (!jvm.Any(x => x is "-cp" or "-classpath" or "--class-path" || x.StartsWith("--class-path=", StringComparison.Ordinal)))
        {
            arguments.Add("-cp");
            arguments.Add(values["classpath"]);
        }

        var logging = version["logging"]?["client"];
        if (logging?["file"] is JsonNode logFile && Text(logFile, "id") is string logId)
        {
            var logPath = SafeCombine(Path.Combine(assetsRoot, "log_configs"), logId);
            Add(logPath, "logging", logFile);
            if (Text(logging, "argument") is string logArgument)
                arguments.Add(Expand(logArgument.Replace("${path}", logPath, StringComparison.Ordinal)));
        }

        if (extraJvmArguments is not null)
            arguments.AddRange(extraJvmArguments.Select(Expand));
        var mainClass = Text(version, "mainClass") ?? throw new InvalidDataException("Version metadata has no mainClass.");
        var mainClassIndex = arguments.Count;
        arguments.Add(mainClass);
        if (version["arguments"]?["game"] is JsonArray game)
            arguments.AddRange(ReadArguments(game, features, java?.Architecture).Select(Expand));
        else if (Text(version, "minecraftArguments") is string legacy)
            arguments.AddRange(SplitLegacyArguments(legacy).Select(Expand));
        else
            warnings.Add("Version metadata has no game arguments.");
        var missingCount = required.Values.Count(x => !x.Exists);
        if (missingCount != 0)
            warnings.Add($"{missingCount} required files are missing; inspect requiredFiles before launching.");
        return new(versionId, java?.Path ?? "java", [.. arguments], gameDirectory, natives, [.. required.Values], [.. warnings.Distinct()], mainClass, uuid, java?.MajorVersion, mainClassIndex)
        {
            AccountId = online ? identity!.Id : null,
            JavaSelection = selectionReport
        };
    }

    public static string OfflineUuid(string playerName)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + playerName));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return Convert.ToHexStringLower(bytes);
    }

    private static IEnumerable<string> ReadArguments(JsonNode? node, IReadOnlyDictionary<string, bool> features, string? javaArchitecture = null)
    {
        if (node is not JsonArray list)
            yield break;
        foreach (var item in list)
        {
            if (item is JsonValue text)
            {
                yield return text.GetValue<string>();
            }
            else if (item is JsonObject condition && MetadataRules.Allows(condition["rules"], features, javaArchitecture))
            {
                if (condition["value"] is JsonArray values)
                {
                    foreach (var value in values)
                        yield return value!.GetValue<string>();
                }
                else if (condition["value"] is JsonValue value)
                {
                    yield return value.GetValue<string>();
                }
            }
        }
    }

    private static IEnumerable<string> SplitLegacyArguments(string input)
    {
        // Metadata uses shell-like quotes, but no shell is invoked or expanded.
        var token = new StringBuilder();
        var quote = '\0';
        var started = false;
        foreach (var ch in input)
        {
            if (ch is '\'' or '"')
            {
                if (quote == '\0')
                {
                    quote = ch;
                    started = true;
                    continue;
                }

                if (quote == ch)
                {
                    quote = '\0';
                    continue;
                }
            }

            if (char.IsWhiteSpace(ch) && quote == '\0')
            {
                if (started)
                {
                    yield return token.ToString();
                    token.Clear();
                    started = false;
                }
            }
            else
            {
                token.Append(ch);
                started = true;
            }
        }

        if (quote != '\0')
            throw new InvalidDataException("Unclosed quote in minecraftArguments.");
        if (started)
            yield return token.ToString();
    }

    private static string? Text(JsonNode? node, string key) => node?[key]?.GetValue<string>();
    private static string LibraryUrl(JsonNode library, string path) => (Text(library, "url") ?? "https://libraries.minecraft.net/").TrimEnd('/') + "/" + path.Replace('\\', '/');
    private static string SafeCombine(string directory, string relative)
    {
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Metadata path escapes its directory: '{relative}'.");
        return path;
    }
}