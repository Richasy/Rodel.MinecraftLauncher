// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Sprout.Graphics;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Servers;

internal sealed record ServerFavorite(
    string Id,
    string Name,
    string Address,
    string[] InstanceIds,
    string? PreferredInstanceId)
{
    internal static ServerFavorite Read(Core.Servers.ServerFavorite value) => new(
        value.Id ?? throw new InvalidDataException("Server favorite ID is missing."),
        value.Name ?? throw new InvalidDataException("Server favorite name is missing."),
        value.Address ?? throw new InvalidDataException("Server favorite address is missing."),
        [.. value.InstanceIds ?? []],
        value.PreferredInstanceId);

    internal string InstanceLabel(IReadOnlyList<InstanceSummary> instances)
    {
        var preferred = PreferredInstanceId ?? (InstanceIds.Length == 1 ? InstanceIds[0] : null);
        return preferred is not null
            ? instances.FirstOrDefault(instance => instance.Id == preferred)?.DisplayName ?? Resources.ServerMissingInstance
            : InstanceIds.Length > 0 ? string.Format(Resources.ServerAssociatedCount, InstanceIds.Length) : Resources.ServerChooseAtConnection;
    }
}

internal sealed record ServerStatus(
    Core.Servers.ServerStatus? Value = null,
    bool Loading = false,
    string? Error = null,
    ImageSource? Icon = null,
    string? IconWarning = null,
    DateTimeOffset? CheckedAt = null)
{
    internal bool Online => Value?.Online == true;
    internal string Title => Loading
        ? Resources.ServerQuerying
        : Online
        ? Resources.ServerOnline
        : Value is not null || Error is not null ? Resources.ServerStatusUnavailable : Resources.ServerNotQueried;
    internal string Tooltip => string.Join(Environment.NewLine, new[] { Title, Online && !Loading ? Detail : null, CheckedAt is { } time ? string.Format(Resources.ServerLastChecked, time.ToLocalTime().ToString("T")) : null, IconWarning is { } warning ? string.Format(Resources.ServerIconWarning, warning) : null, Error ?? Value?.Error, }.Where(value => !string.IsNullOrWhiteSpace(value)));
    internal string Detail
    {
        get
        {
            if (!Online)
                return Value is not null || Error is not null ? Resources.ServerStatusConnectAnyway : string.Empty;
            var status = Value?.Status;
            var version = status?["version"] as JsonObject;
            var players = status?["players"] as JsonObject;
            return string.Format(
                Resources.ServerStatusFacts,
                version?["name"]?.ToString() ?? "—",
                players?["online"]?.ToString() ?? "—",
                players?["max"]?.ToString() ?? "—",
                Value?.LatencyMs?.ToString() ?? "—");
        }
    }
}

internal enum ServerAction
{
    Add,
    Edit,
    Associate,
    Remove,
    Connect,
    Direct,
    ClaimClient
}

internal sealed record ServerConnectionDraft(
    string Address,
    string InstanceId,
    string? AccountId,
    bool Remember);