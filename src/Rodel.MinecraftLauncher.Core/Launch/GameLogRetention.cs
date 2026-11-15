// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Expires recognized launcher console captures and startup timings, never game logs or user exports.</summary>
internal static class GameLogRetention
{
    private static readonly ConditionalWeakTable<LauncherPaths, CleanupSchedule> Schedules = [];

    internal static IReadOnlyList<string> PruneIfDue(LauncherPaths paths)
    {
        var schedule = Schedules.GetValue(paths, static _ => new());
        lock (schedule)
        {
            if (DateTime.UtcNow >= schedule.Next)
            {
                schedule.Warnings = Prune(paths);
                schedule.Next = DateTime.UtcNow.AddDays(1);
            }

            return schedule.Warnings;
        }
    }

    internal static IReadOnlyList<string> Prune(LauncherPaths paths)
    {
        var warnings = new List<string>();
        if (!Directory.Exists(paths.LogsDirectory)) return warnings;
        try
        {
            if ((File.GetAttributes(paths.LogsDirectory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Console cleanup skipped a linked log directory.");
            var cutoff = DateTime.UtcNow.AddDays(-7);
            foreach (var file in Directory.EnumerateFiles(paths.LogsDirectory))
            {
                var name = Path.GetFileName(file);
                if (name.Length < 43 || !Guid.TryParseExact(name[..32], "N", out _)) continue;
                var suffix = name[32..];
                var stream = suffix.StartsWith(".stdout.log", StringComparison.Ordinal) ? ".stdout.log"
                    : suffix.StartsWith(".stderr.log", StringComparison.Ordinal) ? ".stderr.log"
                    : suffix == ".startup.json" ? ".startup.json" : null;
                if (stream is null) continue;
                var ending = suffix[stream.Length..];
                if (ending.Length > 0 && ending != ".status.json"
                    && !(ending.StartsWith(".part", StringComparison.Ordinal) && ending.Length >= 11
                        && ending.AsSpan(5).IndexOfAnyExceptInRange('0', '9') < 0))
                {
                    continue;
                }

                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc >= cutoff) continue;
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Console cleanup skipped a linked capture: " + file);
                    var record = Path.Combine(paths.SessionsDirectory, name[..32] + ".json");
                    if (File.Exists(record))
                    {
                        var session = new SessionService(paths).Get(name[..32]);
                        if (session.ActivityUnknown == true || session.Status is "queued" or "preparing" or "running") continue;
                    }

                    File.Delete(file);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                    or ArgumentException or InvalidOperationException)
                {
                    warnings.Add(error.Message);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            warnings.Add(error.Message);
        }

        foreach (var warning in warnings) Trace.TraceWarning("Console cleanup: {0}", warning);
        return warnings;
    }

    private sealed class CleanupSchedule
    {
        internal DateTime Next { get; set; }
        internal IReadOnlyList<string> Warnings { get; set; } = [];
    }
}