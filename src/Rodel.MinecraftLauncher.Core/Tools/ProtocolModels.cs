// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Rodel.MinecraftLauncher.Core.Tools;

internal sealed record ToolResult(int SchemaVersion, string? Operation, LauncherResultStatus Status, JsonNode? Data, JsonArray Warnings, LauncherOperationError? Error)
{
    /// <summary>Creates a stable failure or cancellation envelope while retaining classified recovery data.</summary>
    /// <param name="operation">The operation name, when command parsing identified one.</param>
    /// <param name="exception">The actual error or cancellation to classify.</param>
    /// <returns>An envelope with the shared Core error classification and no fabricated success payload.</returns>
    public static ToolResult Failure(string? operation, Exception exception)
    {
        var failure = LauncherOperationException.CreateResult(exception);
        return new(failure.SchemaVersion, operation, failure.Status, failure.Data, [], failure.Error);
    }
}

internal sealed record ToolDescription(string Name, string Description, bool ReadOnly, bool Destructive, JsonElement? InputSchema);
internal sealed record ToolContinuation(string Tool, JsonObject Arguments);
internal sealed record ToolSavedResult(string ResultId, ToolContinuation Continuation);
/// <summary>Describes an optional image without including its encoded body in ordinary tool text.</summary>
/// <param name="Kind">The image discriminator used by model clients.</param>
/// <param name="MimeType">The saved image's declared media type.</param>
/// <param name="Continuation">The explicit image read, or null when retention failed.</param>
internal sealed record ToolImageReference(string Kind, string MimeType, ToolContinuation? Continuation);
/// <summary>Reports a completed image read whose binary content travels separately in MCP.</summary>
/// <param name="ResultId">The retained original result identity.</param>
/// <param name="Pointer">The exact image location inside that snapshot.</param>
/// <param name="MimeType">The decoded image's declared media type.</param>
/// <param name="ByteLength">The decoded byte count, excluding base64 transport overhead.</param>
internal sealed record ToolImageInfo(string ResultId, string Pointer, string MimeType, int ByteLength);
internal sealed record ToolPage(JsonArray Items, int Offset, int Limit, int Total, bool HasMore, int? NextOffset, ToolContinuation? Continuation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResultId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Pointer = null);
internal sealed record ToolTruncatedData(bool Truncated, string? ResultId, int TotalCharacters, string Preview, ToolContinuation? Continuation);
internal sealed record ToolItemReference(bool Truncated, string Pointer, ToolContinuation Continuation);
internal sealed record ToolTextChunk(string ResultId, string Pointer, string Text, int Offset, int? NextOffset, bool HasMore, int TotalCharacters, ToolContinuation? Continuation);
internal sealed record IgnoredArgumentsWarning(string Code, string[] Arguments, string Message, ToolContinuation Schema);
internal sealed record ToolObjectNavigation(JsonArray Properties, int PropertyCount, int PropertyOffset, int? NextPropertyOffset);
internal sealed record ToolTruncatedNavigation(JsonArray DataProperties, int DataPropertyCount, int? NextPropertyOffset, string PropertyNavigation);
internal sealed record ToolCapabilities(string Instructions, string Scope, ToolDescription[] Tools, JsonNode Commands, int ResultRetentionDays);
/// <summary>Provides bounded discovery without repeating every tool schema and command's help.</summary>
/// <param name="Tools">The actual registered protocol names.</param>
/// <param name="Topics">The visible root commands available for detailed help.</param>
/// <param name="ResultRetentionDays">The retained-result expiration period.</param>
/// <param name="Details">Guidance for requesting only the needed schema, help or image.</param>
internal sealed record ToolCapabilityIndex(string[] Tools, string[] Topics, int ResultRetentionDays, string Details);
internal sealed record ToolCommandHelp(bool ArgumentsAreShellCommands, Dictionary<string, string[]> Commands, string[] Notes);
internal sealed record ToolContextHeader(string ObservedAt, string? InstanceId, bool GameBridgeConnected);

internal static class ProtocolJson
{
    /// <summary>Serializes a fixed protocol object using explicit source-generated metadata.</summary>
    /// <typeparam name="T">The protocol model whose serialized shape is an object.</typeparam>
    /// <param name="value">The model to serialize.</param>
    /// <param name="typeInfo">The model's source-generated JSON metadata.</param>
    /// <returns>The serialized object node.</returns>
    public static JsonObject Object<T>(T value, JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.SerializeToNode(value, typeInfo)!.AsObject();

    /// <summary>Serializes a tool envelope with the shared result metadata.</summary>
    /// <param name="value">The complete operation envelope.</param>
    /// <returns>The JSON object used by protocol consumers.</returns>
    public static JsonObject Result(ToolResult value) => Object(value, ToolJsonContext.Default.ToolResult);

    /// <summary>Describes a subsequent tool call without invoking it.</summary>
    /// <param name="tool">The protocol tool name to call next.</param>
    /// <param name="arguments">The exact continuation arguments to expose to the client.</param>
    /// <returns>A continuation descriptor, not an executed operation.</returns>
    public static ToolContinuation Continue(string tool, JsonObject arguments) => new(tool, arguments);

    /// <summary>Builds a read-only continuation into an already retained result rather than replaying its operation.</summary>
    /// <param name="resultId">The retained result identifier.</param>
    /// <param name="pointer">An optional JSON Pointer selecting a portion of the saved envelope.</param>
    /// <param name="offset">The initial text or array offset, interpreted by the selected result value.</param>
    /// <returns>A result_read continuation descriptor.</returns>
    public static ToolContinuation Read(string resultId, string? pointer = null, int offset = 0) =>
        new("result_read", Object(new ResultReadInput(resultId, pointer, offset), ToolInputJsonContext.Default.ResultReadInput));

    /// <summary>Adds or replaces protocol fields with detached copies from a typed model.</summary>
    /// <typeparam name="T">The supplemental model to serialize.</typeparam>
    /// <param name="target">The existing protocol object to extend.</param>
    /// <param name="fields">The fields being added.</param>
    /// <param name="typeInfo">The source-generated metadata for the supplemental model.</param>
    public static void Extend<T>(JsonObject target, T fields, JsonTypeInfo<T> typeInfo)
    {
        foreach (var (name, value) in Object(fields, typeInfo))
            target[name] = value?.DeepClone();
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ToolResult))]
[JsonSerializable(typeof(ToolDescription))]
[JsonSerializable(typeof(ToolDescription[]))]
[JsonSerializable(typeof(ToolContinuation))]
[JsonSerializable(typeof(ToolSavedResult))]
[JsonSerializable(typeof(ToolImageReference))]
[JsonSerializable(typeof(ToolImageInfo))]
[JsonSerializable(typeof(ToolCapabilityIndex))]
[JsonSerializable(typeof(ToolPage))]
[JsonSerializable(typeof(ToolTruncatedData))]
[JsonSerializable(typeof(ToolItemReference))]
[JsonSerializable(typeof(ToolTextChunk))]
[JsonSerializable(typeof(IgnoredArgumentsWarning))]
[JsonSerializable(typeof(ToolObjectNavigation))]
[JsonSerializable(typeof(ToolTruncatedNavigation))]
[JsonSerializable(typeof(ToolCapabilities))]
[JsonSerializable(typeof(ToolCommandHelp))]
[JsonSerializable(typeof(ToolContextHeader))]
[JsonSerializable(typeof(Rodel.MinecraftLauncher.Core.Java.JavaRuntimeCatalog))]
[JsonSerializable(typeof(IReadOnlyList<Rodel.MinecraftLauncher.Core.Java.JavaRuntimeSummary>))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class ToolJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CapabilitiesInput))]
[JsonSerializable(typeof(Core.Application.AI.AiRepairRead))]
[JsonSerializable(typeof(Core.Application.AI.AiRepairPlan))]
[JsonSerializable(typeof(Core.Application.AI.AiRepairAction))]
[JsonSerializable(typeof(AppInstanceInput))]
[JsonSerializable(typeof(AppWallpaperInput))]
[JsonSerializable(typeof(AppControlInput))]
[JsonSerializable(typeof(AppMutationRequest))]
[JsonSerializable(typeof(ContextInput))]
[JsonSerializable(typeof(ExecuteInput))]
[JsonSerializable(typeof(PageInput))]
[JsonSerializable(typeof(InstanceInput))]
[JsonSerializable(typeof(RuntimeChangeInput))]
[JsonSerializable(typeof(RuntimeOperationInput))]
[JsonSerializable(typeof(EncodedInstanceInput))]
[JsonSerializable(typeof(GameSettingsSetInput))]
[JsonSerializable(typeof(GameSettingsApplyInput))]
[JsonSerializable(typeof(ResourcepackSelectInput))]
[JsonSerializable(typeof(ShaderpackListInput))]
[JsonSerializable(typeof(ShaderpackSelectInput))]
[JsonSerializable(typeof(InstanceCreateInput))]
[JsonSerializable(typeof(ConfigGetInput))]
[JsonSerializable(typeof(ConfigSetInput))]
[JsonSerializable(typeof(ConfigPatchInput))]
[JsonSerializable(typeof(ContentSearchInput))]
[JsonSerializable(typeof(ContentListInput))]
[JsonSerializable(typeof(ContentInput))]
[JsonSerializable(typeof(ContentInstallInput))]
[JsonSerializable(typeof(ContentReviewInput))]
[JsonSerializable(typeof(ContentApplyInput))]
[JsonSerializable(typeof(ContentReviewShowInput))]
[JsonSerializable(typeof(ContentReviewInstallInput))]
[JsonSerializable(typeof(JobInput))]
[JsonSerializable(typeof(DiagnoseInput))]
[JsonSerializable(typeof(LaunchInput))]
[JsonSerializable(typeof(LaunchStartInput))]
[JsonSerializable(typeof(ProcessStopInput))]
[JsonSerializable(typeof(ReadLogsInput))]
[JsonSerializable(typeof(ResultReadInput))]
[JsonSerializable(typeof(ResultImageReadInput))]
[JsonSerializable(typeof(Dictionary<string, ScalarValue>), TypeInfoPropertyName = "ScalarMap")]
[JsonSerializable(typeof(JsonNode))]
internal sealed partial class ToolInputJsonContext : JsonSerializerContext;