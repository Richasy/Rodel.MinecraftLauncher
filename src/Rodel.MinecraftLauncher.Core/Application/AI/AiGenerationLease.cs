// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal static class AiGenerationLease
{
    public static FileStream Acquire(LauncherPaths paths)
    {
        var directory = Path.Combine(paths.Root, "ai");
        Directory.CreateDirectory(directory);
        try { return new FileStream(Path.Combine(directory, "generation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
        {
            throw new LauncherOperationException("AI_BUSY", "This data root already has an active Core AI request. No second request was queued.");
        }
    }

    public static bool OwnerAlive(JsonObject turn)
    {
        if (turn["runtimeProcessId"]?.GetValue<int>() is not { } pid
            || !DateTimeOffset.TryParse(turn["runtimeStartedUtc"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started))
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && Math.Abs((process.StartTime.ToUniversalTime() - started.UtcDateTime).TotalMilliseconds) <= 1;
        }
        catch (ArgumentException) { return false; }
    }
}