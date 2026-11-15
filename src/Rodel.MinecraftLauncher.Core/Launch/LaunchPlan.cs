// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Java;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Launch;

public sealed record LaunchRequiredFile(string Path, string Kind, string? DownloadUrl = null, string? Sha1 = null, string[]? ExtractExcludes = null)
{
    /// <summary>Gets extraction exclusions as an empty list when none were declared.</summary>
    public string[]? ExtractExcludes { get => field ?? []; init; } = ExtractExcludes;
    public bool Exists => File.Exists(Path);
}

public sealed record LaunchPlan(string VersionId, string JavaPath, string[] Arguments, string WorkingDirectory, string NativesDirectory, LaunchRequiredFile[] RequiredFiles, string[] Warnings, string MainClass, string OfflineUuid, int? JavaMajorVersion = null, int? MainClassArgumentIndex = null)
    : ILaunchResult, Installation.IInstallationResult
{
    public string? AccountId { get; init; }
    /// <summary>Gets whether this launch requests the optional in-game bridge.</summary>
    public bool GameBridgeEnabled { get; init; }
    /// <summary>Gets whether optional bridge preparation must use cached official mappings without network access.</summary>
    public bool GameBridgeOffline { get; init; }
    /// <summary>Gets the runtime metadata root used to prepare exact game bindings.</summary>
    public string? GameBridgeMinecraftDirectory { get; init; }
    /// <summary>Gets the caller's explicit choice to try the same external identity using complete cached authentication material.</summary>
    public bool UseCachedExternalSession { get; init; }
    public string? Server { get; init; }
    public string? ServerConnectionMode { get; init; }
    /// <summary>Gets the resolved Minecraft language option to apply only during actual session preparation.</summary>
    public string? GameLanguage { get; init; }
    /// <summary>Gets the launcher window-mode choice to synchronize with saved game options only when starting a session.</summary>
    public bool? Fullscreen { get; init; }
    public JsonObject? EnvironmentOverrides { get; init; }
    /// <summary>Gets the typed Java recommendation and candidate snapshot associated with this plan.</summary>
    public JavaSelectionReport? JavaSelection { get; init; }
    public MemorySelectionReport? MemorySelection { get; init; }
    public LoggingCompatibilityPlan? LoggingCompatibility { get; init; }
    public string StdoutEncoding { get; init; } = "utf-8";
    public string StderrEncoding { get; init; } = "utf-8";
    public string FileEncoding { get; init; } = "utf-8";
    /// <summary>Gets the argument transport selected for this executable, argument list and Java version.</summary>
    public string CommandLineStrategy => JavaCommandLine.Strategy(JavaPath, Arguments, JavaMajorVersion);

    /// <summary>Serializes the established plan shape without creating a game process.</summary>
    public string ToJson() => ToJsonObject().ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    internal JsonObject ToJsonObject()
    {
        var files = new JsonArray();
        foreach (var file in RequiredFiles)
            files.Add((JsonNode)new JsonObject { ["path"] = file.Path, ["kind"] = file.Kind, ["downloadUrl"] = file.DownloadUrl, ["sha1"] = file.Sha1, ["exists"] = file.Exists, ["extractExcludes"] = new JsonArray([.. (file.ExtractExcludes ?? []).Select(x => (JsonNode?)JsonValue.Create(x))]) });
        return new JsonObject
        {
            ["versionId"] = VersionId,
            ["accountId"] = AccountId,
            ["gameBridgeEnabled"] = GameBridgeEnabled,
            ["gameBridgeOffline"] = GameBridgeOffline,
            ["fullscreen"] = Fullscreen,
            ["gameBridgeMinecraftDirectory"] = GameBridgeMinecraftDirectory,
            ["useCachedExternalSession"] = UseCachedExternalSession,
            ["server"] = Server,
            ["serverConnectionMode"] = ServerConnectionMode,
            ["gameLanguage"] = GameLanguage,
            ["javaPath"] = JavaPath,
            ["environmentOverrides"] = EnvironmentOverrides?.DeepClone(),
            ["javaSelection"] = JsonSerializer.SerializeToNode(JavaSelection, JavaJsonContext.Default.JavaSelectionReport),
            ["memorySelection"] = JsonSerializer.SerializeToNode(MemorySelection, LaunchJsonContext.Default.MemorySelectionReport),
            ["loggingCompatibility"] = JsonSerializer.SerializeToNode(LoggingCompatibility, LaunchJsonContext.Default.LoggingCompatibilityPlan),
            ["stdoutEncoding"] = StdoutEncoding,
            ["stderrEncoding"] = StderrEncoding,
            ["fileEncoding"] = FileEncoding,
            ["arguments"] = new JsonArray([.. Arguments.Select(x => (JsonNode?)JsonValue.Create(x))]),
            ["workingDirectory"] = WorkingDirectory,
            ["nativesDirectory"] = NativesDirectory,
            ["requiredFiles"] = files,
            ["warnings"] = new JsonArray([.. Warnings.Select(x => (JsonNode?)JsonValue.Create(x))]),
            ["mainClass"] = MainClass,
            ["offlineUuid"] = OfflineUuid,
            ["javaMajorVersion"] = JavaMajorVersion,
            ["mainClassArgumentIndex"] = MainClassArgumentIndex,
            ["commandLineStrategy"] = JavaCommandLine.Strategy(JavaPath, Arguments, JavaMajorVersion)
        };
    }
}