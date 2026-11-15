// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Instances;

public static partial class InstanceMigration
{
    private static void ApplyAdapters(string source, string? launcherConfig, string? profileId, ref string game, ref string shared, ref string? version, ref string format, JsonObject settings, JsonObject fields, JsonObject unknown, JsonArray warnings)
    {
        var localId = Path.GetFileName(source);
        if (File.Exists(Path.Combine(source, localId + ".json")))
        {
            version ??= localId;
            if (Directory.GetParent(source)?.Name == "versions")
                shared = Directory.GetParent(source)!.Parent!.FullName;
        }

        void Set(string key, JsonNode? value, string origin)
        {
            if (value is null)
                return;
            settings[key] = value.DeepClone();
            fields[key] = new JsonObject
            {
                ["value"] = value.DeepClone(),
                ["source"] = origin
            };
        }

        void Args(string key, string? raw, string origin)
        {
            if (raw is null)
                return;
            try
            {
                Set(key, Strings(SplitArguments(raw)), origin);
            }
            catch (InvalidDataException ex)
            {
                warnings.Add((JsonNode?)JsonValue.Create(ex.Message));
            }
        }

        var pclFile = Path.Combine(source, "PCL", "Setup.ini");
        if (File.Exists(pclFile))
        {
            format = "pcl";
            var local = ReadIni(pclFile, true);
            var globalFile = launcherConfig ?? Path.Combine(Directory.GetParent(shared)?.FullName ?? shared, "PCL", "Setup.ini");
            var global = File.Exists(globalFile) ? ReadIni(globalFile, true) : [];
            unknown["pclLocal"] = local;
            unknown["pclGlobal"] = global;
            var indie = local["VersionArgumentIndie"]?.ToString();
            if (bool.TryParse(local["VersionArgumentIndieV2"]?.ToString(), out var isolated))
                game = isolated ? source : shared;
            else if (indie is "1" or "2")
                game = indie == "1" ? source : shared;
            else if ((Directory.Exists(Path.Combine(source, "mods")) && Directory.EnumerateFiles(Path.Combine(source, "mods")).Any()) || (Directory.Exists(Path.Combine(source, "saves")) && Directory.EnumerateDirectories(Path.Combine(source, "saves")).Any()))
                game = source;
            else if (global["LaunchArgumentIndieV2"]?.ToString() == "0")
                game = shared;
            else if (global["LaunchArgumentIndieV2"]?.ToString() is null or "4")
                game = source;
            else
                warnings.Add((JsonNode?)JsonValue.Create("PCL automatic release/mod isolation needs version classification; current directory retained, original policy preserved."));
            if (bool.TryParse(local["VersionArgumentIndieV2"]?.ToString(), out _) || indie is "1" or "2" || global["LaunchArgumentIndieV2"]?.ToString() == "0")
                fields["gameDirectoryPolicy"] = JsonValue.Create("PCL explicit isolation");
            foreach (var pair in new[]
            {
                ("LaunchArgumentWindowWidth", "width"),
                ("LaunchArgumentWindowHeight", "height")
            }

            )
            {
                if (int.TryParse(global[pair.Item1]?.ToString(), out var value) && value > 0)
                    Set(pair.Item2, JsonValue.Create(value), globalFile + ":" + pair.Item1);
            }

            if (global["LaunchArgumentWindowType"] is not null)
                Set("fullscreen", JsonValue.Create(global["LaunchArgumentWindowType"]!.ToString() == "0"), globalFile);
            foreach (var pair in new[]
            {
                ("AdvanceJvm", "jvmArguments"),
                ("AdvanceGame", "gameArguments")
            }

            )
            {
                var localArgs = local["Version" + pair.Item1]?.ToString();
                var combined = !string.IsNullOrEmpty(localArgs) ? localArgs : global["Launch" + pair.Item1]?.ToString() ?? string.Empty;
                if (combined.Length > 0)
                    Args(pair.Item2, combined, "PCL effective override:" + pair.Item1);
            }

            var localRam = local["VersionRamType"]?.ToString();
            var manual = localRam == "1" || (localRam is null or "2" && global["LaunchRamType"]?.ToString() == "1");
            var slider = localRam == "1" ? local["VersionRamCustom"] : global["LaunchRamCustom"];
            if (manual && int.TryParse(slider?.ToString(), out var valueRam) && valueRam >= 0)
            {
                var gib = valueRam <= 12 ? (valueRam * .1) + .3 : valueRam <= 25 ? ((valueRam - 12) * .5) + 1.5 : valueRam <= 33 ? valueRam - 25 + 8 : (((double)valueRam - 33) * 2) + 16;
                if (gib * 1024 <= int.MaxValue)
                    Set("maxMemoryMb", JsonValue.Create((int)Math.Round(gib * 1024)), "PCL manual RAM slider");
                else
                    warnings.Add((JsonNode?)JsonValue.Create("PCL memory value exceeds supported size; original value retained, no override applied."));
            }
            else
            {
                warnings.Add((JsonNode?)JsonValue.Create("PCL automatic memory policy is not evaluated; launcher defaults or explicit memory remain usable."));
            }

            var javaRaw = local["VersionArgumentJavaSelect"]?.ToString();
            if (javaRaw?.StartsWith('{') == true && local["VersionArgumentJavaV2"]?.ToString() is null or "3")
            {
                try
                {
                    var java = JsonNode.Parse(javaRaw);
                    if (java?["Path"]?.ToString() is { Length: > 0 } path)
                    {
                        var absolute = Resolve(path, source);
                        if (Path.GetFileName(absolute.TrimEnd('\\', '/')).Equals("bin", StringComparison.OrdinalIgnoreCase))
                            absolute = Path.Combine(absolute, "java.exe");
                        Set("javaPath", JsonValue.Create(absolute), pclFile);
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    warnings.Add((JsonNode?)JsonValue.Create("PCL Java selection cannot be parsed; source value retained."));
                }
            }
            else if (javaRaw is { Length: > 0 })
            {
                warnings.Add((JsonNode?)JsonValue.Create("PCL Java display/global selection retained; supply --java or java select for an explicit runtime."));
            }
        }

        var hmclFile = Path.Combine(source, "hmclversion.cfg");
        if (File.Exists(hmclFile))
        {
            format = "hmcl";
            var local = JsonFiles.ReadObject(hmclFile);
            var global = new JsonObject();
            if (launcherConfig is not null)
            {
                var config = JsonFiles.ReadObject(launcherConfig);
                var profiles = config["configurations"] as JsonObject ?? config["profiles"] as JsonObject;
                var selected = profileId ?? config["selectedProfile"]?.ToString();
                var profile = profileId is not null ? profiles?[profileId] : null;
                if (profileId is null && profiles is not null)
                {
                    var matches = new List<string>();
                    foreach (var pair in profiles)
                    {
                        var directory = pair.Value?["gameDir"]?.ToString();
                        if (string.IsNullOrWhiteSpace(directory))
                            continue;
                        try
                        {
                            if (string.Equals(Resolve(directory, Path.GetDirectoryName(Path.GetFullPath(launcherConfig))!).TrimEnd('\\', '/'), shared.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                                matches.Add(pair.Key);
                        }
                        catch (ArgumentException)
                        {
                        }
                    }

                    if (matches.Count == 1)
                        profile = profiles[matches[0]];
                    else if (selected is not null && (matches.Count == 0 || matches.Contains(selected)))
                        profile = profiles[selected];
                    if (profile is null && profiles.Count == 1)
                        profile = profiles.First().Value;
                }

                if (profile is null)
                    warnings.Add((JsonNode?)JsonValue.Create("HMCL global profile is ambiguous or unavailable; provide --profile PROFILE_ID. Local files remain usable."));
                global = profile?["global"]?.DeepClone() as JsonObject ?? [];
            }

            unknown["hmclLocal"] = local.DeepClone();
            unknown["hmclGlobal"] = global.DeepClone();
            var useGlobal = local["usesGlobal"]?.GetValue<bool>() != false;
            var effective = useGlobal ? global : local;
            if (useGlobal && global.Count == 0)
                warnings.Add((JsonNode?)JsonValue.Create("HMCL uses global settings; provide --launcher-config hmcl.json to resolve them. Local overrides are not falsely applied."));
            foreach (var pair in new[]
            {
                ("maxMemory", "maxMemoryMb"),
                ("minMemory", "minMemoryMb"),
                ("width", "width"),
                ("height", "height"),
                ("fullscreen", "fullscreen")
            }

            )
            {
                if (effective[pair.Item1] is null)
                    continue;
                if (pair.Item1 == "maxMemory" && effective["autoMemory"]?.GetValue<bool>() == true)
                {
                    warnings.Add((JsonNode?)JsonValue.Create("HMCL automatic memory policy retained; no stale fixed maximum is applied."));
                    continue;
                }

                if (pair.Item1 != "fullscreen" && (!int.TryParse(effective[pair.Item1]!.ToString(), out var number) || number <= 0))
                {
                    warnings.Add((JsonNode?)JsonValue.Create("Invalid HMCL " + pair.Item1 + " retained in source settings; no override applied."));
                    continue;
                }

                Set(pair.Item2, effective[pair.Item1], "HMCL " + (useGlobal ? "global" : "local") + ":" + pair.Item1);
            }

            if ((effective["javaVersionType"]?.ToString() == "CUSTOM" || effective["java"]?.ToString() == "Custom") && effective["javaDir"]?.ToString() is { Length: > 0 } java)
                Set("javaPath", JsonValue.Create(Resolve(java, launcherConfig is null ? source : Path.GetDirectoryName(Path.GetFullPath(launcherConfig))!)), "HMCL javaDir");
            Args("jvmArguments", effective["javaArgs"]?.ToString(), "HMCL javaArgs");
            Args("gameArguments", effective["minecraftArgs"]?.ToString(), "HMCL minecraftArgs");
            if (effective["gameDirType"] is not null || effective["gameDir"]?.ToString() is { Length: > 0 })
                fields["gameDirectoryPolicy"] = JsonValue.Create("HMCL explicit isolation");
            if (effective["gameDirType"]?.ToString() is "VERSION_FOLDER" or "1")
                game = source;
            else if (effective["gameDirType"]?.ToString() is "ROOT_FOLDER" or "0")
                game = shared;
            else if (effective["gameDir"]?.ToString() is { Length: > 0 } directory)
                game = Resolve(directory, shared);
        }

        ApplyModernHmcl(source, ref game, ref format, settings, fields, unknown, warnings);

        if (unknown["components"]?["components"] is JsonArray components)
        {
            var dependencies = new JsonObject();
            foreach (var component in components)
            {
                var key = component?["uid"]?.ToString() switch
                {
                    "net.minecraft" => "minecraft",
                    "net.fabricmc.fabric-loader" => "fabric-loader",
                    "org.quiltmc.quilt-loader" => "quilt-loader",
                    "net.minecraftforge" => "forge",
                    "net.neoforged" => "neoforge",
                    _ => null
                };
                if (key is not null)
                    dependencies[key] = component?["version"]?.DeepClone();
            }

            if (dependencies["minecraft"] is not null)
                Set("migrationRuntime", dependencies, "Prism recognized components; instance prepare installs them");
        }
    }
}