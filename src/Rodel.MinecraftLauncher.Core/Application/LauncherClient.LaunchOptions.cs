// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Launch;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    private static LaunchResolution? ResolutionOptions(JsonNode settings, LaunchOptions options)
    {
        var width = options.Width?.ToString() ?? settings["width"]?.ToString();
        var height = options.Height?.ToString() ?? settings["height"]?.ToString();
        return new(int.TryParse(width, out var w) && w > 0 ? w : 1280,
            int.TryParse(height, out var h) && h > 0 ? h : 720);
    }

    private static async Task<LaunchPlan> ApplyLaunchOptionsAsync(LaunchPlan plan, JsonNode settings, LaunchOptions options, InstanceRecord instance, string version, CancellationToken ct)
    {
        var loggingMetadata = await new VersionResolver().ResolveAsync(instance.MinecraftDirectory, version, ct).ConfigureAwait(false);
        var enabled = !bool.TryParse(options.LoggingCompatibility?.ToString() ?? settings["loggingCompatibility"]?.ToString(), out var configured) || configured;
        plan = LoggingCompatibility.Configure(plan, loggingMetadata, enabled);
        plan = LogEncoding.Configure(plan, settings, options.LogEncoding);
        plan = MemorySelection.Configure(plan, settings, options.MemoryMode, options.MaxMemoryMb?.ToString());
        var arguments = plan.Arguments.ToList();
        var warnings = plan.Warnings.ToList();
        foreach (var key in new[]
        {
            "width",
            "height"
        }

        )
        {
            var raw = (key == "width" ? options.Width : options.Height)?.ToString() ?? settings[key]?.ToString();
            if (raw is null)
                continue;
            if (int.TryParse(raw, out var value) && value > 0)
            {
                for (var index = arguments.Count - 1; index >= 0; index--)
                {
                    if (arguments[index] == "--" + key)
                    {
                        arguments.RemoveAt(index);
                        if (index < arguments.Count)
                            arguments.RemoveAt(index);
                    }
                }

                arguments.Add("--" + key);
                arguments.Add(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                warnings.Add("Invalid " + key + " ignored; the game default is retained.");
            }
        }

        if (bool.TryParse(options.Fullscreen?.ToString() ?? settings["fullscreen"]?.ToString(), out var fullscreen))
        {
            arguments.RemoveAll(x => x == "--fullscreen");
            if (fullscreen)
                arguments.Add("--fullscreen");
        }

        var world = options.RepairPlanId is null ? options.World ?? settings["quickPlaySingleplayer"]?.GetValue<string>() : null;
        if (!string.IsNullOrWhiteSpace(world))
        {
            var metadata = await new VersionResolver().ResolveAsync(instance.MinecraftDirectory, version, ct).ConfigureAwait(false);
            if (metadata["arguments"]?["game"]?.ToJsonString().Contains("quickPlaySingleplayer", StringComparison.Ordinal) == true)
            {
                if (options.Server is not null || options.ServerAddress is not null || arguments.Contains("--quickPlayMultiplayer") || arguments.Contains("--server"))
                {
                    warnings.Add("Server connection takes precedence over singleplayer quick play.");
                }
                else
                {
                    arguments.Add("--quickPlaySingleplayer");
                    arguments.Add(world);
                }
            }
            else
            {
                warnings.Add("This version does not advertise singleplayer quick play; normal startup is retained.");
            }
        }

        arguments.AddRange((settings["gameArguments"] as JsonArray ?? []).Select(x => x!.GetValue<string>()));
        arguments.AddRange(options.GameArguments);
        if (options.RepairPlanId is not null)
        {
            // Verification must not enter an original world/server through saved quick-play arguments.
            string[] joins = ["--quickPlaySingleplayer", "--quickPlayMultiplayer", "--quickPlayRealms", "--server", "--port"];
            for (var i = 0; i < arguments.Count; i++)
            {
                if (joins.Contains(arguments[i], StringComparer.Ordinal))
                {
                    arguments.RemoveAt(i);
                    if (i < arguments.Count) arguments.RemoveAt(i);
                    i--;
                }
                else if (joins.Any(flag => arguments[i].StartsWith(flag + "=", StringComparison.Ordinal)))
                {
                    arguments.RemoveAt(i--);
                }
            }
        }

        var environment = settings["environment"]?.DeepClone() as JsonObject ?? [];
        foreach (var (key, value) in options.Environment)
            environment[key] = value is null ? null : JsonValue.Create(value);
        foreach (var key in environment.Select(x => x.Key).ToArray())
        {
            if (string.IsNullOrWhiteSpace(key) || key.Contains('=') || key.Contains('\0') || environment[key]?.ToString().Contains('\0') == true)
            {
                environment.Remove(key);
                warnings.Add("Invalid environment override ignored: " + key);
            }
        }

        return plan with
        {
            GameBridgeEnabled = options.GameBridge ?? (settings["gameBridgeEnabled"]?.GetValue<bool>() == true),
            GameBridgeOffline = options.Offline,
            GameBridgeMinecraftDirectory = instance.MinecraftDirectory,
            UseCachedExternalSession = options.UseCachedExternalSession,
            Fullscreen = bool.TryParse(options.Fullscreen?.ToString() ?? settings["fullscreen"]?.ToString(), out var windowMode) ? windowMode : null,
            GameLanguage = options.GameLanguage is { } language
                ? GameSettingsService.ResolveLaunchLanguage(instance.MinecraftDirectory, loggingMetadata, language) : null,
            Arguments = [.. arguments],
            Warnings = [.. warnings],
            EnvironmentOverrides = environment
        };
    }
}