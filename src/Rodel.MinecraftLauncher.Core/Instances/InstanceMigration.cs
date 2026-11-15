// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rodel.MinecraftLauncher.Core.Instances;

/// <summary>Reads source launch configuration without writing or authenticating against the source launcher.</summary>
public static partial class InstanceMigration
{
    /// <summary>Inspects a source launcher without modification, retaining original unknown configuration and recoverable warnings.</summary>
    public static InstanceImportPreview Preview(string directory, string? profileId = null, string? minecraftDirectory = null, string? launcherConfig = null, string? versionId = null, string? gameDirectory = null)
    {
        var source = Path.GetFullPath(directory);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException(source);
        var game = source;
        var shared = minecraftDirectory is null ? source : Path.GetFullPath(minecraftDirectory);
        var settings = new JsonObject();
        var fields = new JsonObject();
        var unknown = new JsonObject();
        var warnings = new JsonArray();
        void Warn(string message) => warnings.Add((JsonNode)JsonValue.Create(message)!);
        var format = "minecraft-directory";
        string? version = null;
        string? name = null;
        void Field(string key, JsonNode? value, string origin)
        {
            if (value is null)
                return;
            fields[key] = new JsonObject
            {
                ["value"] = value.DeepClone(),
                ["source"] = origin
            };
        }

        void Setting(string key, JsonNode? value, string origin)
        {
            if (value is null)
                return;
            settings[key] = value.DeepClone();
            Field(key, value, origin);
        }

        var prismFile = Path.Combine(source, "instance.cfg");
        if (File.Exists(prismFile))
        {
            format = "prism";
            game = Directory.Exists(Path.Combine(source, ".minecraft")) ? Path.Combine(source, ".minecraft") : Directory.Exists(Path.Combine(source, "minecraft")) ? Path.Combine(source, "minecraft") : source;
            var root = Directory.GetParent(source)?.Parent?.FullName ?? source;
            if (minecraftDirectory is null)
                shared = Directory.Exists(Path.Combine(root, "libraries")) ? root : game;
            var instance = ReadIni(prismFile);
            var globalFile = Path.Combine(root, "prismlauncher.cfg");
            var global = File.Exists(globalFile) ? ReadIni(globalFile) : [];
            name = instance["name"]?.GetValue<string>();
            Field("name", JsonValue.Create(name), "instance.cfg:name");
            string? Read(string key, string flag, out string origin)
            {
                var local = string.Equals(instance[flag]?.GetValue<string>(), "true", StringComparison.OrdinalIgnoreCase);
                origin = local ? "instance.cfg:" + key : "prismlauncher.cfg:" + key;
                return (local ? instance : global)[key]?.GetValue<string>();
            }

            var java = Read("JavaPath", "OverrideJavaLocation", out var javaSource);
            if (!string.IsNullOrWhiteSpace(java))
                Setting("javaPath", JsonValue.Create(Resolve(java, javaSource.StartsWith("instance") ? source : root)), javaSource);
            var jvm = Read("JvmArgs", "OverrideJavaArgs", out var jvmSource);
            if (jvm is not null)
            {
                try
                {
                    Setting("jvmArguments", Strings(SplitArguments(jvm)), jvmSource);
                }
                catch (InvalidDataException error)
                {
                    Warn(error.Message + " Raw source value retained.");
                }
            }

            foreach (var (sourceKey, key) in new[]
            {
                ("MinMemAlloc", "minMemoryMb"),
                ("MaxMemAlloc", "maxMemoryMb")
            }

            )
            {
                var text = Read(sourceKey, "OverrideMemory", out var origin);
                if (int.TryParse(text, out var memory) && memory > 0)
                    Setting(key, JsonValue.Create(memory), origin);
                else if (text is not null)
                    Warn("Invalid " + origin + " was retained as unknown; no memory value inferred.");
            }

            unknown["instanceConfig"] = instance;
            unknown["globalConfig"] = global;
            var components = Path.Combine(source, "mmc-pack.json");
            if (File.Exists(components))
            {
                var pack = JsonFiles.ReadObject(components);
                unknown["components"] = pack.DeepClone();
                version = (pack["components"] as JsonArray)?.FirstOrDefault(item => item?["uid"]?.GetValue<string>() == "net.minecraft")?["version"]?.GetValue<string>();
                Field("versionId", JsonValue.Create(version), "mmc-pack.json:components[uid=net.minecraft].version");
                if ((pack["components"] as JsonArray)?.Any(item => item?["uid"]?.GetValue<string>() is string uid && uid != "net.minecraft" && uid != "org.lwjgl3" && uid != "org.lwjgl") == true)
                    Warn("Prism component patches are retained in sourceSettings but are not converted into standard version metadata. Select an existing custom version or use instance prepare to install recognized components before launching.");
            }
        }
        else
        {
            if (!File.Exists(Path.Combine(source, "launcher_profiles.json")))
            {
                foreach (var child in new[]
                {
                    ".minecraft",
                    "minecraft"
                }

                )
                {
                    if (Directory.Exists(Path.Combine(source, child)))
                    {
                        game = Path.Combine(source, child);
                        if (minecraftDirectory is null)
                            shared = game;
                        break;
                    }
                }
            }

            var profilesFile = Path.Combine(game, "launcher_profiles.json");
            if (File.Exists(profilesFile))
            {
                format = "minecraft-profiles";
                var document = JsonFiles.ReadObject(profilesFile);
                var profiles = document["profiles"] as JsonObject ?? [];
                var candidates = new JsonArray(profiles.Select(pair => (JsonNode)new JsonObject { ["id"] = pair.Key, ["name"] = pair.Value?["name"]?.DeepClone(), ["versionId"] = pair.Value?["lastVersionId"]?.DeepClone() }).ToArray());
                if (profileId is null && profiles.Count == 1)
                    profileId = profiles.First().Key;
                if (profileId is not null)
                {
                    var profile = profiles[profileId] as JsonObject ?? throw new ArgumentException("Unknown Minecraft profile: " + profileId);
                    name = profile["name"]?.GetValue<string>();
                    version = profile["lastVersionId"]?.GetValue<string>();
                    if (profile["gameDir"]?.GetValue<string>() is { Length: > 0 } selectedGame)
                        game = Resolve(selectedGame, Path.GetDirectoryName(profilesFile)!);
                    if (minecraftDirectory is null)
                        shared = Path.GetDirectoryName(profilesFile)!;
                    Field("versionId", JsonValue.Create(version), "launcher_profiles.json:profiles." + profileId + ".lastVersionId");
                    if (profile["javaDir"]?.GetValue<string>() is { Length: > 0 } java)
                        Setting("javaPath", JsonValue.Create(Resolve(java, Path.GetDirectoryName(profilesFile)!)), "launcher_profiles.json:javaDir");
                    if (profile["javaArgs"]?.GetValue<string>() is string args)
                    {
                        string[] parsed;
                        try
                        {
                            parsed = [.. SplitArguments(args)];
                        }
                        catch (InvalidDataException error)
                        {
                            Warn(error.Message + " Raw source value retained.");
                            parsed = [];
                        }

                        var retained = new List<string>();
                        foreach (var arg in parsed)
                        {
                            var match = MyRegex().Match(arg);
                            if (match.Success && long.TryParse(match.Groups[2].Value, out var number) && number > 0 && number * (match.Groups[3].Value.Equals("g", StringComparison.OrdinalIgnoreCase) ? 1024 : 1) <= int.MaxValue)
                                Setting(match.Groups[1].Value == "x" ? "maxMemoryMb" : "minMemoryMb", JsonValue.Create((int)(number * (match.Groups[3].Value.Equals("g", StringComparison.OrdinalIgnoreCase) ? 1024 : 1))), "launcher_profiles.json:javaArgs");
                            else
                                retained.Add(arg);
                        }

                        Setting("jvmArguments", Strings(retained), "launcher_profiles.json:javaArgs");
                    }

                    unknown["profile"] = profile.DeepClone();
                }
                else
                {
                    Warn("Multiple Minecraft profiles exist. Select --profile explicitly; no profile settings or game directory were guessed.");
                }

                unknown["profiles"] = candidates;
            }
        }

        string[] versions = [];
        try
        {
            if (Directory.Exists(Path.Combine(shared, "versions")))
                versions = [.. Directory.EnumerateDirectories(Path.Combine(shared, "versions")).Where(path => File.Exists(Path.Combine(path, Path.GetFileName(path) + ".json"))).Select(path => Path.GetFileName(path)).Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn("Local versions could not be enumerated; explicit version selection remains available. " + ex.Message);
        }

        version = versionId ?? version;
        if (version is null)
        {
            if (versions.Length == 1)
            {
                version = versions[0];
                Field("versionId", JsonValue.Create(version), "versions directory:single local metadata");
            }
        }

        var adapterSource = SelectedVersionDirectory(source, game, shared, version);
        try
        {
            ApplyAdapters(adapterSource, launcherConfig, profileId, ref game, ref shared, ref version, ref format, settings, fields, unknown, warnings);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            unknown["sourceSpecificError"] = ex.Message;
            Warn("Some launcher-specific settings could not be read. Standard directories and readable fields are retained; explicit configuration remains available. " + ex.Message);
        }

        if (minecraftDirectory is not null)
            shared = Path.GetFullPath(minecraftDirectory);
        var directoryCandidates = new List<string>();
        var needsGameDirectory = false;
        if (adapterSource != source && gameDirectory is null
            && unknown["profile"]?["gameDir"] is null
            && fields["gameDirectoryPolicy"] is null
            && (format is "hmcl" or "pcl" || Directory.Exists(Path.Combine(adapterSource, "mods")) || Directory.Exists(Path.Combine(adapterSource, "saves"))))
        {
            needsGameDirectory = true;
            directoryCandidates.Add(shared);
            directoryCandidates.Add(adapterSource);
            Warn("The selected version contains game data but has no readable isolation policy. Select its game directory explicitly.");
        }

        if (!string.IsNullOrWhiteSpace(gameDirectory))
            game = Path.GetFullPath(gameDirectory);

        Field("gameDirectory", JsonValue.Create(game), format == "prism" ? "instance directory game child" : "selected directory/profile.gameDir");
        Field("minecraftDirectory", JsonValue.Create(shared), minecraftDirectory is not null ? "explicit --minecraft-directory" : "source layout/shared dependencies");
        if (!Directory.Exists(game))
            Warn("Selected game directory does not exist; create it explicitly before importing.");
        if (version is not null && !File.Exists(Path.Combine(shared, "versions", version, version + ".json")))
            Warn("Selected version has no standard local metadata in the shared root; import remains possible, but launching requires metadata or installation.");
        return new(format, source, game, shared, version, versions, needsGameDirectory, directoryCandidates, name, profileId, settings, fields, unknown, warnings);
    }

    private static string Resolve(string path, string root) => Path.GetFullPath(path, root);
    private static JsonArray Strings(IEnumerable<string> items) => new([.. items.Select(item => (JsonNode?)JsonValue.Create(item))]);
    private static JsonObject ReadIni(string file, bool allowColon = false)
    {
        var result = new JsonObject();
        foreach (var line in File.ReadLines(file))
        {
            var text = line.Trim();
            if (text.Length == 0 || text[0] is '#' or ';' or '[')
                continue;
            var separator = text.IndexOf('=');
            var colon = allowColon ? text.IndexOf(':') : -1;
            if (colon > 0 && (separator < 0 || colon < separator))
                separator = colon;
            if (separator > 0)
                result[text[..separator]] = text[(separator + 1)..];
        }

        return result;
    }

    public static IEnumerable<string> SplitArguments(string input)
    {
        var value = new StringBuilder();
        var quote = '\0';
        var started = false;
        for (var index = 0; index < input.Length; index++)
        {
            var ch = input[index];
            if (ch == '\\' && index + 1 < input.Length && input[index + 1] == '"')
            {
                value.Append('"');
                started = true;
                index++;
                continue;
            }

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
                    yield return value.ToString();
                    value.Clear();
                    started = false;
                }
            }
            else
            {
                value.Append(ch);
                started = true;
            }
        }

        if (quote != '\0')
            throw new InvalidDataException("Source JVM arguments contain an unclosed quote. Provide explicit JVM arguments instead of importing this field.");
        if (started)
            yield return value.ToString();
    }

    [GeneratedRegex(@"^-Xm([sx])(\d+)([mMgG])$")]
    private static partial Regex MyRegex();
}