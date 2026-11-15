// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>A live packaged App discovered for the selected data root, without private pipe credentials.</summary>
public sealed record AppControlInstance
{
    /// <summary>Gets the stable discovery identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the live process identifier.</summary>
    public int ProcessId { get; init; }
    /// <summary>Gets the process start-time ticks used to reject recycled process identifiers.</summary>
    public long StartedTicks { get; init; }
    /// <summary>Gets the matching canonical data root.</summary>
    public required string DataRoot { get; init; }
    /// <summary>Gets the verified OS package identity.</summary>
    public required string PackageIdentity { get; init; }
}

/// <summary>A discovery record that could not be verified as a live matching App.</summary>
public sealed record AppControlDiscoveryWarning
{
    /// <summary>Gets the stable discovery failure code.</summary>
    public string Code { get; init; } = "APP_DISCOVERY_UNAVAILABLE";
    /// <summary>Gets the discovery file name without its private contents.</summary>
    public required string Record { get; init; }
    /// <summary>Gets the underlying discovery diagnostic.</summary>
    public required string Message { get; init; }
}

/// <summary>Live Apps and explicit discovery warnings for one data root.</summary>
public sealed record AppControlList
{
    /// <summary>Gets live matching Apps in discovery order.</summary>
    public required AppControlInstance[] Items { get; init; }
    /// <summary>Gets stale or malformed registration diagnostics.</summary>
    public required AppControlDiscoveryWarning[] Warnings { get; init; }
}

/// <summary>Supported semantic actions in an explicitly selected running App, not arbitrary UI method invocation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppControlAction>))]
public enum AppControlAction
{
    State,
    PreferencesUpdate,
    Navigate,
    WindowSet,
    Close,
    WallpaperList,
    WallpaperSelect,
    WallpaperImport,
    WallpaperRename,
    WallpaperRemove,
    ImageCacheStatus,
    ImageCacheClear
}

/// <summary>Explicit package preference edits; omitted fields preserve the player's current choice.</summary>
public sealed record AppPreferenceUpdate
{
    /// <summary>Gets System, Light or Dark.</summary>
    public string? Theme { get; init; }
    /// <summary>Gets a canonical launcher UI language tag from <see cref="LauncherLanguages.Supported"/>.</summary>
    public string? Language { get; init; }
    /// <summary>Gets Tray, Taskbar or KeepVisible.</summary>
    public string? AfterLaunch { get; init; }
    /// <summary>Gets Exit or Tray.</summary>
    public string? CloseBehavior { get; init; }
    /// <summary>Gets an absolute data directory for the next App start; empty restores the default without moving data.</summary>
    public string? DataDirectory { get; init; }
}

/// <summary>Shared App operation parameters; omitted fields never manufacture an operation or permission.</summary>
public record AppControlParameters
{
    /// <summary>Gets the observed App state fingerprint required before mutations.</summary>
    public string? ExpectedFingerprint { get; init; }
    /// <summary>Gets explicit preference fields to change.</summary>
    public AppPreferenceUpdate? Preferences { get; init; }
    /// <summary>Gets a stable page key for navigation.</summary>
    public string? Page { get; init; }
    /// <summary>Gets an optional settings category key.</summary>
    public string? Category { get; init; }
    /// <summary>Gets an explicit existing instance to select.</summary>
    public string? InstanceId { get; init; }
    /// <summary>Gets normal, maximized, minimized or tray.</summary>
    public string? WindowState { get; init; }
    /// <summary>Gets light or dark for wallpaper actions.</summary>
    public string? Theme { get; init; }
    /// <summary>Gets a wallpaper identifier from the App's real catalog.</summary>
    public string? WallpaperId { get; init; }
    /// <summary>Gets the explicitly selected image source for import.</summary>
    public string? Source { get; init; }
    /// <summary>Gets the custom wallpaper's new display name.</summary>
    public string? Name { get; init; }
    /// <summary>Gets explicit confirmation for closing the App or removing its custom wallpaper.</summary>
    public bool Confirm { get; init; }
}

/// <summary>One semantic App operation; identity and current-state checks remain independent of model input.</summary>
public sealed record AppControlRequest : AppControlParameters
{
    /// <summary>Gets the concrete operation, never a class/member name.</summary>
    public required AppControlAction Action { get; init; }
}

internal sealed record AppControlRegistration(string Id, int ProcessId, long StartedTicks, string DataRoot,
    string PackageIdentity, string PipeName, string Nonce);

/// <summary>The fixed authenticated request frame; only the App operation determines the meaning of its typed parameters.</summary>
internal sealed record AppControlRequestFrame(int Version, string HostId, string Nonce, string RequestId, AppControlRequest Request);

/// <summary>The fixed response envelope around an App-owned open payload.</summary>
internal sealed record AppControlResponseFrame(string? RequestId, string HostId, string? Status, JsonNode? Data,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SchemaVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LauncherOperationError? Error = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppControlRequest))]
[JsonSerializable(typeof(AppPreferenceUpdate))]
[JsonSerializable(typeof(AppControlList))]
[JsonSerializable(typeof(AppControlDiscoveryWarning[]))]
public sealed partial class AppControlJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppControlRegistration))]
[JsonSerializable(typeof(AppControlRequestFrame))]
[JsonSerializable(typeof(AppControlResponseFrame))]
internal sealed partial class AppControlWireJsonContext : JsonSerializerContext;