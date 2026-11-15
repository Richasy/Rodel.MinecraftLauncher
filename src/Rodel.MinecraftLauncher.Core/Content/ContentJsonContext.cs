// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Rodel.MinecraftLauncher.Core.Content;

/// <summary>Provides source-generated serialization metadata for content inspection and advisory reports.</summary>
/// <remarks>Use <see cref="Entry"/>, <see cref="Entries"/>, <see cref="SourceDetection"/>, and <see cref="Analysis"/>
/// for report serialization so unrequested fields remain absent while attempted, unsuccessful reads retain explicit nulls.</remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata,
    AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(ContentEntry))]
[JsonSerializable(typeof(IReadOnlyList<ContentEntry>))]
[JsonSerializable(typeof(ContentSourceDetection))]
[JsonSerializable(typeof(ContentAnalysis))]
[JsonSerializable(typeof(ContentCompatibility))]
[JsonSerializable(typeof(ContentResult))]
[JsonSerializable(typeof(ContentInstallReceipt))]
[JsonSerializable(typeof(ContentReview))]
[JsonSerializable(typeof(BundledModCatalog))]
[JsonSerializable(typeof(DependencyRepairReview))]
[JsonSerializable(typeof(ContentUpdatesPlan))]
[JsonSerializable(typeof(ContentPlanNode))]
[JsonSerializable(typeof(ContentCheckProgress))]
[JsonSerializable(typeof(IReadOnlyList<ContentPlanFinding>))]
[JsonSerializable(typeof(ContentAnalysisFinding))]
[JsonSerializable(typeof(ResourcePackSelectionResult))]
[JsonSerializable(typeof(Configuration.GameSettingsChangeResult))]
[JsonSerializable(typeof(IReadOnlyList<Configuration.ResourcePackMapping>))]
[JsonSerializable(typeof(Application.ResourcePackSelectionSummary))]
[JsonSerializable(typeof(Application.ShaderPackSelectionSummary))]
[JsonSerializable(typeof(Application.ShaderPackSelectionResult))]
[JsonSerializable(typeof(System.Text.Json.Nodes.JsonNode))]
public sealed partial class ContentJsonContext : JsonSerializerContext
{
    private static readonly JsonSerializerOptions SerializationOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        TypeInfoResolver = new ContentJsonContext().WithAddedModifier(ConfigurePresence)
    };

    /// <summary>Gets content-entry metadata preserving unrequested fields versus explicit unknown read results.</summary>
    public static JsonTypeInfo<ContentEntry> Entry => (JsonTypeInfo<ContentEntry>)SerializationOptions.GetTypeInfo(typeof(ContentEntry));
    /// <summary>Gets inventory metadata using the same conditional field contract as individual entries.</summary>
    public static JsonTypeInfo<IReadOnlyList<ContentEntry>> Entries => (JsonTypeInfo<IReadOnlyList<ContentEntry>>)SerializationOptions.GetTypeInfo(typeof(IReadOnlyList<ContentEntry>));
    /// <summary>Gets source-detection metadata preserving each candidate's conditional fields.</summary>
    public static JsonTypeInfo<ContentSourceDetection> SourceDetection => (JsonTypeInfo<ContentSourceDetection>)SerializationOptions.GetTypeInfo(typeof(ContentSourceDetection));
    /// <summary>Gets advisory report metadata preserving inventory and loader-specific dependency fields.</summary>
    public static JsonTypeInfo<ContentAnalysis> Analysis => (JsonTypeInfo<ContentAnalysis>)SerializationOptions.GetTypeInfo(typeof(ContentAnalysis));
    /// <summary>Gets dependency metadata preserving the TOML-only nullable relation fields.</summary>
    public static JsonTypeInfo<ContentDependencyDeclaration> DependencyDeclaration => (JsonTypeInfo<ContentDependencyDeclaration>)SerializationOptions.GetTypeInfo(typeof(ContentDependencyDeclaration));
    /// <summary>Gets resource-pack selection metadata preserving the installed inventory's conditional fields.</summary>
    public static JsonTypeInfo<Application.ResourcePackSelectionSummary> ResourcePackSelection => (JsonTypeInfo<Application.ResourcePackSelectionSummary>)SerializationOptions.GetTypeInfo(typeof(Application.ResourcePackSelectionSummary));
    /// <summary>Gets shader selection metadata with the same optional inventory fields as ordinary content reads.</summary>
    public static JsonTypeInfo<Application.ShaderPackSelectionSummary> ShaderPackSelection => (JsonTypeInfo<Application.ShaderPackSelectionSummary>)SerializationOptions.GetTypeInfo(typeof(Application.ShaderPackSelectionSummary));
    /// <summary>Gets shader selection results while preserving nested content field presence.</summary>
    public static JsonTypeInfo<Application.ShaderPackSelectionResult> ShaderPackSelectionOutcome => (JsonTypeInfo<Application.ShaderPackSelectionResult>)SerializationOptions.GetTypeInfo(typeof(Application.ShaderPackSelectionResult));
    /// <summary>Gets metadata for the closed content operation result variants.</summary>
    public static JsonTypeInfo<ContentResult> Result => (JsonTypeInfo<ContentResult>)SerializationOptions.GetTypeInfo(typeof(ContentResult));
    /// <summary>Gets metadata for persisted content reviews and their conditional fields.</summary>
    public static JsonTypeInfo<ContentReview> Review => (JsonTypeInfo<ContentReview>)SerializationOptions.GetTypeInfo(typeof(ContentReview));
    /// <summary>Gets metadata for verified dependency reviews.</summary>
    public static JsonTypeInfo<DependencyRepairReview> DependencyReview => (JsonTypeInfo<DependencyRepairReview>)SerializationOptions.GetTypeInfo(typeof(DependencyRepairReview));
    /// <summary>Gets metadata for a frozen installation plan.</summary>
    public static JsonTypeInfo<ContentInstallPlan> InstallPlan => (JsonTypeInfo<ContentInstallPlan>)SerializationOptions.GetTypeInfo(typeof(ContentInstallPlan));
    /// <summary>Gets receipt metadata using the same field-presence rules as the closed content result contract.</summary>
    public static JsonTypeInfo<ContentInstallReceipt> InstallReceipt => (JsonTypeInfo<ContentInstallReceipt>)SerializationOptions.GetTypeInfo(typeof(ContentInstallReceipt));
    /// <summary>Gets plan-finding metadata preserving explicitly supplied null evidence and unknown saved fields.</summary>
    public static JsonTypeInfo<IReadOnlyList<ContentPlanFinding>> PlanFindings => (JsonTypeInfo<IReadOnlyList<ContentPlanFinding>>)SerializationOptions.GetTypeInfo(typeof(IReadOnlyList<ContentPlanFinding>));
    /// <summary>Gets one local analysis finding's exact optional-field contract.</summary>
    public static JsonTypeInfo<ContentAnalysisFinding> AnalysisFinding => (JsonTypeInfo<ContentAnalysisFinding>)SerializationOptions.GetTypeInfo(typeof(ContentAnalysisFinding));
    /// <summary>Gets metadata for an exact candidate at the tracking-document boundary.</summary>
    public static JsonTypeInfo<ContentPlanNode> PlanNode => (JsonTypeInfo<ContentPlanNode>)SerializationOptions.GetTypeInfo(typeof(ContentPlanNode));

    private static void ConfigurePresence(JsonTypeInfo info)
    {
        if (info.Type == typeof(ContentPlanFinding))
        {
            foreach (var property in info.Properties)
            {
                if (!property.IsExtensionData)
                    property.ShouldSerialize = (value, _) => ((ContentPlanFinding)value).PresentFields.Contains(property.Name);
            }
        }

        if (info.Type == typeof(ContentAnalysisFinding))
        {
            foreach (var property in info.Properties)
            {
                if (property.Name == "nestedPath")
                    property.ShouldSerialize = (value, _) => ((ContentAnalysisFinding)value).Kind == "nested-archive-not-indexed";
                if (property.Name == "range")
                    property.ShouldSerialize = (value, _) => ((ContentAnalysisFinding)value).Kind is "declared-breaks" or "declared-conflicts";
            }
        }

        if (info.Type == typeof(ContentPlanNode))
        {
            foreach (var property in info.Properties)
            {
                if (property.Name is "title" or "iconUrl")
                    property.ShouldSerialize = (value, _) => ((ContentPlanNode)value).PresentationRead;
            }
        }

        if (info.Type == typeof(ContentPlanEdge))
        {
            foreach (var property in info.Properties)
            {
                if (property.Name == "requestedVersion")
                    property.ShouldSerialize = (value, _) => ((ContentPlanEdge)value).RequestedVersionPresent;
            }
        }

        if (info.Type == typeof(ContentReview))
        {
            foreach (var property in info.Properties)
            {
                if (property.Name == "plan") property.ShouldSerialize = (value, _) => ((ContentReview)value).Mode is "online" or "builtin";
                if (property.Name == "planningFailure") property.ShouldSerialize = (value, _) => ((ContentReview)value).Operation == "update";
            }
        }

        if (info.Type == typeof(ContentReviewItem))
        {
            foreach (var property in info.Properties)
            {
                if (property.Name is "projectId" or "currentVersionId" or "currentVersion" or "enabled" or "pinned" or "channel")
                    property.ShouldSerialize = (value, _) => ((ContentReviewItem)value).Source is null;
                if (property.Name is "targetVersion" or "targetFileName")
                    property.ShouldSerialize = (value, _) => ((ContentReviewItem)value).Status == "available";
                if (property.Name == "warning")
                    property.ShouldSerialize = (value, _) => ((ContentReviewItem)value).WarningPresent;
            }
        }

        if (info.Type == typeof(ContentEntry))
        {
            foreach (var property in info.Properties)
            {
                property.ShouldSerialize = property.Name switch
                {
                    "platform" or "projectId" or "versionId" or "downloadUrl" or "gameVersion" or "loader" or "pinned" or "channel"
                        or "upstream" or "dependencies" or "dependency" or "sha512" or "title" or "iconUrl" or "selectionAliases" or "provides" or "requestedBy"
                        => (entry, _) => ((ContentEntry)entry).TrackingFields.Contains(property.Name),
                    "sha1" or "size" or "metadataFormat" => (entry, value) => value is not null || ((ContentEntry)entry).ReadFailed,
                    "metadata" => (entry, value) => value is not null || ((ContentEntry)entry).ReadFailed || ((ContentEntry)entry).MetadataPresent,
                    "activeDirectory" or "activeFileName" => (entry, value) => value is not null || ((ContentEntry)entry).DisabledDirectory,
                    "isMod" or "archiveReadable" or "metadataWarning" or "readWarning" or "tracking" => (_, value) => value is not null,
                    "resourcePackSelection" => (entry, _) => ((ContentEntry)entry).SelectionAttempted,
                    _ => property.ShouldSerialize
                };
                var setter = property.Set;
                if (setter is not null && property.Name is "platform" or "projectId" or "versionId" or "downloadUrl" or "gameVersion" or "loader" or "pinned" or "channel"
                    or "upstream" or "dependencies" or "dependency" or "sha512" or "title" or "iconUrl" or "selectionAliases" or "provides" or "requestedBy")
                {
                    property.Set = (entry, value) => { setter(entry, value); ((ContentEntry)entry).TrackingFields.Add(property.Name); };
                }

                if (setter is not null && property.Name == "metadata")
                    property.Set = (entry, value) => { setter(entry, value); ((ContentEntry)entry).MetadataPresent = true; };
                if (setter is not null && property.Name is "sha1" or "size" or "metadataFormat")
                    property.Set = (entry, value) => { setter(entry, value); if (value is null) ((ContentEntry)entry).ReadFailed = true; };
                if (setter is not null && property.Name is "activeDirectory" or "activeFileName")
                    property.Set = (entry, value) => { setter(entry, value); ((ContentEntry)entry).DisabledDirectory = true; };
            }
        }

        if (info.Type == typeof(ContentDependencyDeclaration))
        {
            foreach (var property in info.Properties)
            {
                if (property.Name is "type" or "side")
                    property.ShouldSerialize = (entry, _) => ((ContentDependencyDeclaration)entry).Toml;
            }
        }
    }
}