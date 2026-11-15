// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Per-launch overrides. Omitted values inherit instance/global settings.</summary>
public sealed record LaunchOptions
{
    // Internal room ownership is recorded before the monitor can create a process; never serialized as a launch override.
    internal Func<string, Task>? SessionCreated { get; init; }
    internal string? RepairPlanId { get; init; }
    public string? VersionId { get; init; }
    public string? JavaPath { get; init; }
    public string? AccountId { get; init; }
    /// <summary>False freezes a caller-confirmed identity, including an explicit player without a saved account.</summary>
    public bool InheritIdentity { get; init; } = true;
    public string? PlayerName { get; init; }
    public int? MaxMemoryMb { get; init; }
    public string? MemoryMode { get; init; }
    public string? LogEncoding { get; init; }
    /// <summary>Gets the Minecraft locale to write before process start; omission preserves the player's game language.</summary>
    public string? GameLanguage { get; init; }
    public bool? LoggingCompatibility { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool? Fullscreen { get; init; }
    public string? World { get; init; }
    public string? Server { get; init; }
    /// <summary>A concrete multiplayer address, already selected by the caller. Never reinterpreted as a favorite ID.</summary>
    public string? ServerAddress { get; init; }
    /// <summary>Whether a dedicated client inherits its owning server when no explicit server or world was selected.</summary>
    public bool InheritServer { get; init; } = true;
    public IReadOnlyList<string> JvmArguments { get; init; } = [];
    public IReadOnlyList<string> GameArguments { get; init; } = [];
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();
    public bool PlanOnly { get; init; }
    public bool Offline { get; init; }
    /// <summary>Gets an explicit bridge enablement override; omission inherits the instance setting.</summary>
    public bool? GameBridge { get; init; }
    /// <summary>Explicitly skips external session refresh only when the same role, protected token, metadata and component are cached.</summary>
    public bool UseCachedExternalSession { get; init; }
    public bool WaitForExit { get; init; }
}