// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Launch;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Runtime;

/// <summary>Common recognized runtime selection information, including manually configured fallbacks.</summary>
/// <param name="InstanceId">Owning instance.</param>
/// <param name="MinecraftVersion">Resolved or best-known base game version.</param>
/// <param name="Loader">Recognized or configured loader.</param>
/// <param name="Source">Evidence used to describe this selection.</param>
[JsonDerivedType(typeof(ResolvedRuntimeStatus))]
[JsonDerivedType(typeof(FallbackRuntimeStatus))]
public abstract record RuntimeStatus(string InstanceId, string? MinecraftVersion, string? Loader, string Source)
{
    /// <summary>Gets the selected launch profile when its metadata resolved successfully.</summary>
    [JsonIgnore]
    public virtual string? VersionId => null;
    /// <summary>Gets the recognized loader version when metadata resolved successfully.</summary>
    [JsonIgnore]
    public virtual string? LoaderVersion => null;
}

/// <summary>Describes a successfully resolved runtime without exposing the full version document.</summary>
public sealed record ResolvedRuntimeStatus : RuntimeStatus
{
    /// <summary>Builds the public selection summary from resolved third-party metadata.</summary>
    public ResolvedRuntimeStatus(string instanceId, RuntimeDescription description)
        : this(instanceId, description.MinecraftVersion, description.Loader, description.Source, description.VersionId, description.LoaderVersion, description.MainClass, description.JavaVersion)
    {
    }

    /// <summary>Restores a flattened resolved-runtime snapshot through source-generated metadata.</summary>
    [JsonConstructor]
    public ResolvedRuntimeStatus(string instanceId, string? minecraftVersion, string? loader, string source, string? versionId, string? loaderVersion, JsonNode? mainClass, JsonNode? javaVersion)
        : base(instanceId, minecraftVersion, loader, source)
    {
        VersionId = versionId;
        LoaderVersion = loaderVersion;
        MainClass = mainClass;
        JavaVersion = javaVersion;
    }

    /// <inheritdoc/>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public override string? VersionId { get; }
    /// <inheritdoc/>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public override string? LoaderVersion { get; }
    /// <summary>Gets the main-class declaration without discarding unusual third-party values.</summary>
    public JsonNode? MainClass { get; }
    /// <summary>Gets the open third-party Java requirement declaration.</summary>
    public JsonNode? JavaVersion { get; }
}

/// <summary>Retains usable compatibility advice when selected metadata cannot be fully resolved.</summary>
/// <param name="InstanceId">Owning instance.</param>
/// <param name="LaunchVersion">Selected profile identifier.</param>
/// <param name="MinecraftVersion">Best-known base game version.</param>
/// <param name="Loader">Configured or recognized loader.</param>
/// <param name="Source">Origin of compatibility advice.</param>
/// <param name="Warnings">Compatibility resolution warnings.</param>
/// <param name="Warning">Failure to resolve the full runtime.</param>
/// <param name="LoaderSource">Optional evidence for the loader.</param>
public sealed record FallbackRuntimeStatus(string InstanceId, string? LaunchVersion, string? MinecraftVersion, string? Loader, string Source, IReadOnlyList<string> Warnings, string Warning,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LoaderSource = null)
    : RuntimeStatus(InstanceId, MinecraftVersion, Loader, Source);

/// <summary>A recoverable runtime operation, retaining unknown persisted fields and settings snapshots.</summary>
public sealed class RuntimeOperationSummary
{
    /// <summary>Gets the operation identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the stored operation state, or unreadable for a failed record.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? State { get; init; }
    /// <summary>Gets the comparison between current selection and saved snapshots.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ObservedState { get; init; }
    /// <summary>Gets unverified when a broken record cannot establish instance ownership.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ownership { get; init; }
    /// <summary>Gets the individual read failure without hiding other operations.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Warning { get; init; }
    /// <summary>Gets the persisted UTC creation timestamp.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreatedAt { get; init; }
    /// <summary>Gets the saved target description, retaining historical and unknown metadata.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Target { get; init; }
    /// <summary>Gets persisted snapshots and unknown operation fields preserved verbatim.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];
}

/// <summary>Lists runtime operations including independently unreadable records.</summary>
/// <param name="InstanceId">Instance whose known operations were selected.</param>
/// <param name="Operations">Readable and unverified unreadable records.</param>
public sealed record RuntimeOperationList(string InstanceId, IReadOnlyList<RuntimeOperationSummary> Operations);

/// <summary>Source-generated serialization metadata for runtime read summaries.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RuntimeStatus))]
[JsonSerializable(typeof(ResolvedRuntimeStatus))]
[JsonSerializable(typeof(RuntimeOperationSummary))]
[JsonSerializable(typeof(RuntimeOperationList))]
[JsonSerializable(typeof(RuntimeTarget))]
[JsonSerializable(typeof(RuntimeChangeResult))]
[JsonSerializable(typeof(RuntimePreparationResult))]
[JsonSerializable(typeof(RuntimeRollbackResult))]
public partial class RuntimeJsonContext : JsonSerializerContext;