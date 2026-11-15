// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Launch;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal static class HomeLaunchSettingsService
{
    internal static async Task<LaunchSettingsDraft> ReadAsync(LauncherClient client, InstanceSummary selected)
    {
        var instance = client.Instances.Get(selected.Id);
        var values = client.Settings.GetEffective(instance).Values;
        var overrides = client.Settings.GetOverrides(instance);
        var bridge = overrides.TryGetPropertyValue("gameBridgeEnabled", out var bridgeValue)
            ? bridgeValue is not null && bridgeValue.GetValue<bool>() ? GameBridgeSettingMode.Enabled : GameBridgeSettingMode.Disabled
            : GameBridgeSettingMode.Inherit;
        var globalBridgeValue = client.Settings.GetEffective().Values["gameBridgeEnabled"];
        var globalBridge = globalBridgeValue is not null && globalBridgeValue.GetValue<bool>();
        var automaticMemory = values["memoryMode"]?.ToString() == "auto";
        var player = values["playerName"]?.ToString() ?? "Player";
        if (values["accountId"]?.ToString() is { } identity)
        {
            player = client.Identities.List().Select(entry => entry.Identity).FirstOrDefault(i => i?.Id == identity)?.Name ?? player;
        }

        var version = values["versionId"]?.ToString() ?? instance.VersionId ?? string.Empty;
        var unavailable = false;
        string? repair = null;
        if (!string.IsNullOrWhiteSpace(version))
        {
            try
            {
                await new VersionResolver().ResolveAsync(instance.MinecraftDirectory, version).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                unavailable = true;
                if (instance.VersionId is { Length: > 0 } original && original != version)
                {
                    try
                    {
                        await new VersionResolver().ResolveAsync(instance.MinecraftDirectory, original).ConfigureAwait(false);
                        repair = original;
                    }
                    catch (Exception recoveryError) when (recoveryError is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                    {
                    }
                }
            }
        }

        return new(
            selected.Id,
            selected.DisplayName,
            version,
            values["javaPath"]?.ToString() ?? instance.JavaPath ?? string.Empty,
            values["maxMemoryMb"]?.ToString() ?? "2048",
            player,
            Baseline: new(values["javaPath"]?.ToString() ?? instance.JavaPath ?? string.Empty, values["maxMemoryMb"]?.ToString() ?? "2048", player, bridge, automaticMemory),
            PhysicalMemoryMb: MemorySelection.PhysicalMb(),
            AvailableMemoryMb: MemorySelection.AvailableMb(),
            VersionUnavailable: unavailable,
            RepairVersion: repair,
            GameBridge: bridge,
            GlobalGameBridge: globalBridge,
            AutomaticMemory: automaticMemory);
    }

    internal static bool TryValidate(LaunchSettingsDraft draft, out int memory)
    {
        memory = 0;
        if (!draft.CanSave)
            return false;
        if (draft.MemoryChanged)
            memory = checked((int)draft.MemoryValue);
        return true;
    }

    internal static async Task SaveAsync(LauncherClient client, LaunchSettingsDraft draft, int memory)
    {
        if (!TryValidate(draft, out var validatedMemory) || validatedMemory != memory)
            throw new ArgumentException(draft.ValidationError);
        var instance = client.Instances.Get(draft.InstanceId);
        var changes = new JsonObject();
        var remove = new List<string>();
        if (draft.RepairVersionSelected && draft.RepairVersion is { } repaired)
        {
            if (instance.VersionId != repaired || draft.Version != repaired)
                throw new InvalidOperationException(Resources.LaunchVersionUnavailable);
            await new VersionResolver().ResolveAsync(instance.MinecraftDirectory, repaired).ConfigureAwait(false);
            changes["versionId"] = repaired;
        }
        // Only explicit edits create overrides. In particular, an unrelated save must
        // preserve a managed Java reference, manual heap value and the current identity.
        if (draft.JavaChanged)
        {
            changes["javaPath"] = string.IsNullOrWhiteSpace(draft.Java) ? null : JsonValue.Create(draft.Java.Trim());
        }

        if (draft.MemoryChanged)
            changes["maxMemoryMb"] = memory;
        if (draft.MemoryModeChanged)
            changes["memoryMode"] = draft.AutomaticMemory ? "auto" : "manual";
        if (draft.GameBridgeChanged)
        {
            if (draft.GameBridge == GameBridgeSettingMode.Inherit)
                remove.Add("gameBridgeEnabled");
            else
                changes["gameBridgeEnabled"] = draft.GameBridge == GameBridgeSettingMode.Enabled;
        }

        if (draft.PlayerChanged)
        {
            var identity = await client.Identities.AddOfflineAsync(draft.Player.Trim()).ConfigureAwait(false);
            changes["accountId"] = identity.Id;
        }

        await client.Settings.UpdateAsync(changes, remove, instance).ConfigureAwait(false);
    }
}