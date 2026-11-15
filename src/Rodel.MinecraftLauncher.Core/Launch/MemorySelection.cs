// Copyright (c) Richasy. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Windows.Win32;
using Windows.Win32.System.SystemInformation;

namespace Rodel.MinecraftLauncher.Core.Launch;

public static class MemorySelection
{
    /// <summary>Reads currently available physical memory; a failed query remains an advisory unknown.</summary>
    public static long? AvailableMb()
    {
        var status = new MEMORYSTATUSEX
        {
            dwLength = (uint)Unsafe.SizeOf<MEMORYSTATUSEX>()
        };
        return PInvoke.GlobalMemoryStatusEx(ref status) ? (long)(status.ullAvailPhys / (1024 * 1024)) : null;
    }

    /// <summary>Returns installed physical memory for input validation; unavailable queries stay advisory.</summary>
    public static long? PhysicalMb()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Unsafe.SizeOf<MEMORYSTATUSEX>() };
        return PInvoke.GlobalMemoryStatusEx(ref status) ? (long)(status.ullTotalPhys / (1024 * 1024)) : null;
    }

    /// <summary>Calculates advisory heap sizing without overriding explicit user arguments or blocking launch.</summary>
    public static MemoryRecommendation Recommend(long? availableMb, int reserveMb = 1024, int minimumMb = 512, int maximumMb = 16384, bool java32Bit = false)
    {
        var upper = java32Bit ? Math.Min(maximumMb, 1408) : maximumMb;
        var budget = availableMb is { } available ? Math.Max(128, (available - Math.Max(0, reserveMb)) * 7 / 10) : 2048;
        var suggested = (int)Math.Max(128, Math.Min(upper, Math.Min(Math.Max(minimumMb, budget), availableMb ?? long.MaxValue)));
        return new(availableMb, reserveMb, minimumMb, maximumMb, java32Bit, suggested,
            availableMb is null ? "query-fallback" : "windows-available-physical-memory", true,
            availableMb is null ? "Windows memory query failed; configured/default memory remains available." : null);
    }

    /// <summary>Reads available memory and applies the effective automatic-memory bounds for launch and configuration previews.</summary>
    public static MemoryRecommendation RecommendConfigured(JsonNode settings, bool java32Bit = false)
    {
        int Setting(string key, int fallback, int minimum = 1) => int.TryParse(settings[key]?.ToString(), out var value) && value >= minimum ? value : fallback;
        return Recommend(AvailableMb(), Setting("memoryReserveMb", 1024, 0), Setting("autoMemoryMinMb", 512), Setting("autoMemoryMaxMb", 16384), java32Bit);
    }

    public static LaunchPlan Configure(LaunchPlan plan, JsonNode settings, string? modeOverride, string? explicitMemory)
    {
        var mode = modeOverride ?? settings["memoryMode"]?.ToString() ?? "auto";
        var warnings = plan.Warnings.ToList();
        if (mode is not ("manual" or "auto"))
        {
            warnings.Add("Unknown memory mode retained as manual for this launch.");
            mode = "manual";
        }

        int Setting(string key, int fallback, int minimum = 1) => int.TryParse(settings[key]?.ToString(), out var value) && value >= minimum ? value : fallback;
        var selected = plan.JavaSelection?.Candidates.FirstOrDefault(candidate => candidate.Path == plan.JavaPath);
        var recommendation = RecommendConfigured(settings, selected?.Bitness == 32);
        var arguments = plan.Arguments.ToArray();
        var index = plan.MainClassArgumentIndex ?? Array.IndexOf(arguments, plan.MainClass);
        var custom = arguments.Take(index).Skip(1).LastOrDefault(x => x.StartsWith("-Xmx", StringComparison.Ordinal));
        var source = explicitMemory is not null ? "explicit" : custom is not null ? "custom-jvm" : mode == "auto" ? "auto" : "configured";
        var mb = explicitMemory is not null && int.TryParse(explicitMemory, out var requested) && requested > 0 ? requested : mode == "auto" && recommendation.AvailableMb is not null ? recommendation.SuggestedMb : Setting("maxMemoryMb", 2048);
        // A user's JVM Xmx remains the final authority unless --memory explicitly overrides it.
        if (explicitMemory is not null)
        {
            for (var i = 0; i < index; i++)
            {
                if (arguments[i].StartsWith("-Xmx", StringComparison.Ordinal))
                    arguments[i] = "-Xmx" + mb + "M";
            }
        }
        else if (custom is null && mode == "auto")
        {
            arguments[0] = "-Xmx" + mb + "M";
        }

        if (custom is null || explicitMemory is not null)
        {
            for (var i = 0; i < index; i++)
            {
                if (arguments[i].StartsWith("-Xms", StringComparison.Ordinal) && ParseMb(arguments[i][4..]) is { } initial && initial > mb)
                {
                    arguments[i] = "-Xms" + mb + "M";
                    warnings.Add("Initial heap reduced to the selected maximum.");
                }
            }
        }

        var effective = explicitMemory is null && custom is not null ? ParseMb(custom[4..]) : mb;
        if (effective is { } heap && recommendation.AvailableMb is { } available && heap > available)
            warnings.Add("Requested heap exceeds currently available physical memory; the explicit request is retained.");
        return plan with
        {
            Arguments = arguments,
            MemorySelection = new(recommendation, mode, source, effective, custom),
            Warnings = [.. warnings]
        };
    }

    public static int? ParseMb(string raw)
    {
        if (raw.Length == 0)
            return null;
        var suffix = char.ToUpperInvariant(raw[^1]);
        var number = char.IsLetter(suffix) ? raw[..^1] : raw;
        if (!long.TryParse(number, out var value) || value < 0)
            return null;
        var mb = suffix switch
        {
            'G' => value * 1024,
            'M' => value,
            'K' => value / 1024,
            _ => value / (1024 * 1024)
        };
        return mb <= int.MaxValue ? (int)mb : null;
    }
}