// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Launch;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed record HomeInstanceDetails(
    string? Version,
    string? Loader,
    int? EnabledMods,
    string? ContentIssue,
    bool ManualJava,
    int? MemoryMb,
    bool ManagedJava = false,
    string? LaunchIssue = null)
{
    // A summary failure is not an execution decision: Core can prepare missing versions
    // and reports an unreadable local profile through the launch task.
    internal bool CanLaunch => LaunchIssue is null or "metadata";
    internal string LaunchBlockReason => LaunchIssue switch
    {
        "version" => Resources.LaunchVersionRequired,
        _ => string.Empty,
    };
    internal string Runtime => string.Format(Resources.RuntimeSummary, Version ?? Resources.VersionNotConfigured, LoaderDisplay, ContentIssue == "missing"
            ? Resources.ContentDirectoryMissing
            : ContentIssue == "failed" ? Resources.ContentReadFailed : string.Format(Resources.EnabledMods, EnabledMods));
    internal string LoaderDisplay => Loader switch
    {
        "vanilla" or "none" => Resources.Vanilla,
        null when LaunchIssue == "metadata" => Resources.LaunchVersionFilesUnavailable,
        null when LaunchIssue == "version" => Resources.VersionNotConfigured,
        null => Resources.LoaderUnknown,
        _ => Loader,
    };
    internal string Configuration => string.Format(Resources.ConfigurationSummary, ManagedJava
            ? Resources.JavaManaged
            : ManualJava ? Resources.JavaManual : Resources.JavaAutomatic, MemoryMb is { } mb ? string.Format(Resources.MemoryLimit, (decimal)mb / 1024) : Resources.MemoryUnknown);
}

/// <summary>Read-only summaries from Core; launch preparation remains responsible for executable readiness.</summary>
internal static class HomeInstanceReader
{
    internal static InstanceSummary Summarize(Core.Instances.InstanceSummary registration) => new(
        registration.Id,
        registration.Name ?? string.Empty,
        registration.VersionId ?? string.Empty,
        registration.Status == "unreadable",
        registration.Icon?["data"]?.ToString(),
        registration.ServerId);

    internal static async Task<InstanceSummary> ReadSummaryAsync(LauncherClient client, Core.Instances.InstanceSummary registration, CancellationToken ct)
    {
        var summary = Summarize(registration);
        if (summary.Unreadable)
            return summary;
        try
        {
            var context = await client.GetContentContextAsync(registration.Id, ct: ct).ConfigureAwait(false);
            summary = summary with
            {
                Version = context.MinecraftVersion ?? summary.Version,
                Loader = DisplayLoader(context.Loader ?? (context.Source == "version-metadata" ? "vanilla" : null))
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
        }

        try
        {
            if (client.Packs.IsManaged(registration.Id) && client.Packs.GetState(registration.Id).Pack?.Name is JsonValue name && name.TryGetValue<string>(out var packName))
            {
                summary = summary with
                {
                    PackName = packName
                };
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
        }

        return summary;
    }

    private static string? DisplayLoader(string? loader) => loader switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        "none" => "vanilla",
        _ => loader,
    };

    internal static async Task<HomeInstanceDetails> ReadAsync(LauncherClient client, string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var instance = client.Instances.Get(id);
        var effective = client.Settings.GetEffective(instance);
        var values = effective.Values;
        var versionId = values["versionId"]?.ToString() ?? instance.VersionId;
        var launchIssue = string.IsNullOrWhiteSpace(versionId) ? "version" : null;
        if (launchIssue is null)
        {
            try
            {
                await new VersionResolver().ResolveAsync(instance.MinecraftDirectory, versionId!, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                launchIssue = "metadata";
            }
        }

        var context = await client.GetContentContextAsync(id, ct: ct).ConfigureAwait(false);
        var loader = DisplayLoader(context.Loader ?? (context.Source == "version-metadata" ? "vanilla" : null));
        var version = context.MinecraftVersion;
        string? contentIssue = null;
        int? enabled = null;
        if (!Directory.Exists(instance.GameDirectory))
        {
            contentIssue = "missing";
        }
        else
        {
            try
            {
                var mods = await client.Content.ListAsync(instance.GameDirectory, "mods", includeHashes: false, ct: ct).ConfigureAwait(false);
                enabled = mods.Count(n => n.Enabled == true && n.IsMod == true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                contentIssue = "failed";
            }
        }

        var java = values["javaPath"]?.ToString() ?? instance.JavaPath;
        int? memory = int.TryParse(values["maxMemoryMb"]?.ToString(), out var mb) && mb > 0 ? mb : null;
        if (values["memoryMode"]?.ToString() == "auto")
        {
            var recommendation = MemorySelection.RecommendConfigured(values);
            if (recommendation.AvailableMb is not null)
                memory = recommendation.SuggestedMb;
        }

        return new(
            version,
            loader,
            enabled,
            contentIssue,
            !string.IsNullOrWhiteSpace(java),
            memory,
            !string.IsNullOrWhiteSpace(values["javaRuntimeId"]?.ToString()),
            launchIssue);
    }
}