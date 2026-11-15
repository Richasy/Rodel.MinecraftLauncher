// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal enum PreferenceKind
{
    Text,
    Integer,
    Boolean,
    MemoryMode,
    Arguments,
    Environment,
    Proxy,
    DownloadSource,
    Mirrors
}

/// <summary>Deliberately selected launcher preferences; not a reflection-generated catalog.</summary>
internal sealed record PreferenceField(
    string Key,
    string Category,
    Func<string> Title,
    PreferenceKind Kind = PreferenceKind.Text,
    string Default = "",
    int Minimum = 1,
    int Maximum = int.MaxValue,
    Func<string>? Description = null);

internal static class CorePreferenceFields
{
    internal static readonly PreferenceField[] All = [new(
            "fullscreen",
            "launch",
            () => Resources.SettingsFullscreen,
            PreferenceKind.Boolean,
            "false"), new(
            "width",
            "launch",
            () => Resources.SettingsWindowWidth,
            PreferenceKind.Integer,
            "1280"), new(
            "height",
            "launch",
            () => Resources.SettingsWindowHeight,
            PreferenceKind.Integer,
            "720"), new("quickPlaySingleplayer", "launch", () => Resources.SettingsQuickWorld, Description: () => Resources.SettingsQuickWorldHelp), new(
            "gameBridgeEnabled",
            "launch",
            () => Resources.BridgeTitle,
            PreferenceKind.Boolean,
            "true",
            Description: () => Resources.BridgeSettingsHelp), new("javaPath", "java", () => Resources.LaunchJavaPath, Description: () => Resources.SettingsJavaAutomatic), new(
            "memoryMode",
            "java",
            () => Resources.SettingsMemoryMode,
            PreferenceKind.MemoryMode,
            "auto"), new(
            "minMemoryMb",
            "java",
            () => Resources.SettingsMinimumMemory,
            PreferenceKind.Integer,
            "512"), new(
            "maxMemoryMb",
            "java",
            () => Resources.LaunchMemory,
            PreferenceKind.Integer,
            "2048"), new(
            "memoryReserveMb",
            "java",
            () => Resources.SettingsMemoryReserve,
            PreferenceKind.Integer,
            "1024",
            0), new(
            "autoMemoryMinMb",
            "java",
            () => Resources.SettingsAutoMemoryMinimum,
            PreferenceKind.Integer,
            "512"), new(
            "autoMemoryMaxMb",
            "java",
            () => Resources.SettingsAutoMemoryMaximum,
            PreferenceKind.Integer,
            "16384"), new(
            "network.source",
            "network",
            () => Resources.SettingsDownloadSource,
            PreferenceKind.DownloadSource,
            "official",
            Description: () => Resources.SettingsDownloadSourceHelp), new(
            "network.proxy",
            "network",
            () => Resources.SettingsProxy,
            PreferenceKind.Proxy,
            Description: () => Resources.SettingsProxyHelp), new(
            "network.concurrency",
            "network",
            () => Resources.SettingsConcurrency,
            PreferenceKind.Integer,
            "4",
            1,
            64,
            Description: () => Resources.SettingsConcurrencyHelp), new(
            "network.connectTimeoutSeconds",
            "network",
            () => Resources.SettingsConnectTimeout,
            PreferenceKind.Integer,
            "20",
            1,
            3600), new(
            "network.requestTimeoutSeconds",
            "network",
            () => Resources.SettingsRequestTimeout,
            PreferenceKind.Integer,
            "30",
            1,
            3600), new(
            "network.readIdleSeconds",
            "network",
            () => Resources.SettingsReadIdle,
            PreferenceKind.Integer,
            "120",
            1,
            3600), new(
            "network.metadataTimeoutSeconds",
            "network",
            () => Resources.SettingsMetadataTimeout,
            PreferenceKind.Integer,
            "30",
            1,
            3600), new(
            "network.mirrors",
            "network",
            () => Resources.SettingsMirrors,
            PreferenceKind.Mirrors,
            Description: () => Resources.SettingsMirrorsHelp), new(
            "loggingCompatibility",
            "logs",
            () => Resources.SettingsLoggingCompatibility,
            PreferenceKind.Boolean,
            "true",
            Description: () => Resources.SettingsLoggingCompatibilityHelp), new("stdoutEncoding", "logs", () => Resources.SettingsOutputEncoding, Description: () => Resources.SettingsEncodingHelp), new("stderrEncoding", "logs", () => Resources.SettingsErrorEncoding, Description: () => Resources.SettingsEncodingHelp), new(
            "jvmArguments",
            "advanced",
            () => Resources.SettingsJvmArguments,
            PreferenceKind.Arguments,
            Description: () => Resources.SettingsArgumentLines), new(
            "gameArguments",
            "advanced",
            () => Resources.SettingsGameArguments,
            PreferenceKind.Arguments,
            Description: () => Resources.SettingsArgumentLines), new(
            "environment",
            "advanced",
            () => Resources.SettingsEnvironment,
            PreferenceKind.Environment,
            Description: () => Resources.SettingsEnvironmentHelp), ];

    internal static PreferenceField[] For(string category) => [.. All.Where(field => field.Category == category)];
}