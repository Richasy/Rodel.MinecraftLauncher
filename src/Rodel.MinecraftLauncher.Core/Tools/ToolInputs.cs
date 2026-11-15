// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Tools;

internal sealed record CapabilitiesInput(string? ToolName = null, string? Topic = null);
internal sealed record ContextInput(string? InstanceId = null, int Limit = 10);
internal sealed record ExecuteInput(string[] Arguments);
internal sealed record AppInstanceInput(string AppId);
internal sealed record AppWallpaperInput(string AppId, string Theme);
internal sealed record AppControlInput(string AppId, AppMutationRequest Request);

[JsonConverter(typeof(JsonStringEnumConverter<AppMutationAction>))]
internal enum AppMutationAction
{
    PreferencesUpdate = (int)AppControlAction.PreferencesUpdate,
    Navigate = (int)AppControlAction.Navigate,
    WindowSet = (int)AppControlAction.WindowSet,
    Close = (int)AppControlAction.Close,
    WallpaperSelect = (int)AppControlAction.WallpaperSelect,
    WallpaperImport = (int)AppControlAction.WallpaperImport,
    WallpaperRename = (int)AppControlAction.WallpaperRename,
    WallpaperRemove = (int)AppControlAction.WallpaperRemove,
    ImageCacheClear = (int)AppControlAction.ImageCacheClear
}

internal sealed record AppMutationRequest : AppControlParameters
{
    /// <summary>Gets a mutation-only action; read operations have their own contracts and schema.</summary>
    public required AppMutationAction Action { get; init; }
}
internal sealed record PageInput(int Offset = 0, int Limit = 20);
internal record InstanceInput(string InstanceId);
internal sealed record RuntimeChangeInput(string InstanceId, string? MinecraftVersion = null, string? Loader = null, string? LoaderVersion = null, string? Version = null, string? JavaPath = null, bool Plan = false, bool Offline = false, bool Background = false);
internal sealed record RuntimeOperationInput(string InstanceId, string OperationId);
internal sealed record EncodedInstanceInput(string InstanceId, string? Encoding = null);
internal sealed record GameSettingsSetInput(string InstanceId, Dictionary<string, ScalarValue> Changes, bool Plan = false, string? ExpectedFingerprint = null, string? Encoding = null);
internal sealed record GameSettingsApplyInput(string InstanceId, bool Overwrite = false);
internal sealed record ResourcepackSelectInput(string InstanceId, string[] Names, bool Replace = false, bool AllowIncompatible = false, bool Plan = false, string? ExpectedFingerprint = null, string? Encoding = null);
internal sealed record ShaderpackListInput(string InstanceId, string? Engine = null);
internal sealed record ShaderpackSelectInput(string InstanceId, string? FileName = null, string? Engine = null, bool Plan = false, string? ExpectedFingerprint = null);
internal sealed record InstanceCreateInput(string Name, string Version);
internal sealed record ConfigGetInput(string? InstanceId = null, string? Key = null);
internal sealed record ConfigSetInput(string Key, JsonElement Value, string? InstanceId = null);
internal sealed record ConfigPatchInput(JsonObject? Changes = null, string[]? RemoveKeys = null, string? InstanceId = null);
internal sealed record ContentSearchInput(string Query, string? Platform = null, string? ProjectType = null, string? MinecraftVersion = null, string? Loader = null, string? Index = null, int Offset = 0, int Limit = 20);
internal sealed record ContentListInput(string InstanceId, string? Kind = null, string? World = null, int Offset = 0, int Limit = 20);
internal record ContentInput(string InstanceId, string ProjectId, string? VersionId = null, string? Platform = null, string? Kind = null, string? World = null, string? MinecraftVersion = null, string? Loader = null, string? Channel = null, bool IncludeDependencies = true, Dictionary<string, string>? VersionSelections = null);
internal sealed record ContentInstallInput(string InstanceId, string ProjectId, string? VersionId = null, string? Platform = null, string? Kind = null, string? World = null, string? MinecraftVersion = null, string? Loader = null, string? Channel = null, bool IncludeDependencies = true, Dictionary<string, string>? VersionSelections = null, string? ExpectedLockFingerprint = null, bool Background = false)
    : ContentInput(InstanceId, ProjectId, VersionId, Platform, Kind, World, MinecraftVersion, Loader, Channel, IncludeDependencies, VersionSelections);
internal sealed record ContentReviewInput(string InstanceId, string[] Files, string? Kind = null, string? World = null, string? MinecraftVersion = null, string? Loader = null, string? Channel = null, bool IncludeDependencies = true, Dictionary<string, string>? VersionSelections = null, string? ExpectedLockFingerprint = null, bool UseSourceContext = false, Dictionary<string, string>? SourceSelections = null);
internal sealed record ContentApplyInput(string InstanceId, string ReviewId, bool Confirm = false, bool Background = false);
internal sealed record ContentReviewShowInput(string InstanceId, string ReviewId);
internal sealed record ContentReviewInstallInput(string InstanceId, string ProjectId, string? VersionId = null, string? Platform = null, string? Kind = null, string? World = null, string? MinecraftVersion = null, string? Loader = null, string? Channel = null, bool IncludeDependencies = true, Dictionary<string, string>? VersionSelections = null)
    : ContentInput(InstanceId, ProjectId, VersionId, Platform, Kind, World, MinecraftVersion, Loader, Channel, IncludeDependencies, VersionSelections);
internal sealed record JobInput(string JobId);
internal sealed record DiagnoseInput(string? InstanceId = null, string? SessionId = null, string? ReportPath = null, bool Verify = false);
internal record LaunchInput(string InstanceId, string? JavaPath = null, string? Version = null, string? Player = null, int? MemoryMb = null, int? Width = null, int? Height = null, bool? Fullscreen = null, string? World = null, string? Server = null, string[]? GameArguments = null, Dictionary<string, ScalarValue>? Environment = null, string? MemoryMode = null, string? LogEncoding = null, bool? LoggingCompatibility = null, string? AccountId = null, string? ServerAddress = null, bool NoServer = false, string? GameLanguage = null);
internal sealed record LaunchStartInput(string InstanceId, string? JavaPath = null, string? Version = null, string? Player = null, int? MemoryMb = null, int? Width = null, int? Height = null, bool? Fullscreen = null, string? World = null, string? Server = null, string[]? GameArguments = null, Dictionary<string, ScalarValue>? Environment = null, string? MemoryMode = null, string? LogEncoding = null, bool? LoggingCompatibility = null, bool Offline = false, bool Background = false, string? AccountId = null, string? ServerAddress = null, bool NoServer = false, string? GameLanguage = null, bool? GameBridge = null)
    : LaunchInput(InstanceId, JavaPath, Version, Player, MemoryMb, Width, Height, Fullscreen, World, Server, GameArguments, Environment, MemoryMode, LogEncoding, LoggingCompatibility, AccountId, ServerAddress, NoServer, GameLanguage);
internal sealed record ProcessStopInput(string SessionId, bool Force = false);
internal sealed record ReadLogsInput(string SessionId, int Tail = 100, int Bytes = 16384, long? StdoutOffset = null, long? StderrOffset = null, string? Format = null, string? MinimumLevel = null, JsonObject? ParserState = null);
internal sealed record ResultReadInput(string ResultId, string? Pointer = null, int Offset = 0, int Limit = 20, int Characters = 8000, int PropertyOffset = 0);
/// <summary>Selects one previously retained image without repeating its source operation.</summary>
/// <param name="ResultId">The snapshot identity from an image reference.</param>
/// <param name="Pointer">The exact RFC 6901 location supplied by that reference.</param>
internal sealed record ResultImageReadInput(string ResultId, string Pointer);

/// <summary>A JSON scalar, retaining number precision, empty strings and explicit null.</summary>
[JsonConverter(typeof(ScalarValueConverter))]
internal readonly record struct ScalarValue(JsonElement Value)
{
    internal static readonly JsonValueKind[] AllowedKinds =
        [JsonValueKind.String, JsonValueKind.Number, JsonValueKind.True, JsonValueKind.False, JsonValueKind.Null];

    /// <summary>Formats a scalar as a CLI value, preserving number precision and empty strings while returning null for JSON null.</summary>
    /// <returns>The literal argument value, or null to represent removal where the consuming command supports it.</returns>
    public string? ToArgument() => Value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => Value.GetString(),
        _ => Value.GetRawText()
    };
}

internal sealed class ScalarValueConverter : JsonConverter<ScalarValue>
{
    /// <summary>Reads one JSON scalar without coercion and rejects arrays or objects.</summary>
    /// <param name="reader">The reader positioned at the scalar value.</param>
    /// <param name="typeToConvert">The scalar wrapper type requested by the serializer.</param>
    /// <param name="options">The active serializer options.</param>
    /// <returns>A wrapper retaining the original scalar kind and representation.</returns>
    public override ScalarValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = JsonElement.ParseValue(ref reader);
        if (!ScalarValue.AllowedKinds.Contains(value.ValueKind))
            throw new JsonException("Expected a string, number, boolean or null.");
        return new(value);
    }

    /// <summary>Writes the retained JSON value directly without string conversion or numeric coercion.</summary>
    /// <param name="writer">The destination JSON writer.</param>
    /// <param name="value">The scalar wrapper to serialize.</param>
    /// <param name="options">The active serializer options.</param>
    public override void Write(Utf8JsonWriter writer, ScalarValue value, JsonSerializerOptions options) => value.Value.WriteTo(writer);
}