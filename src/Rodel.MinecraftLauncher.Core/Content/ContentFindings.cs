// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Content;

/// <summary>Reports one content-plan conflict, advisory or unresolved requirement with its exact provenance.</summary>
public sealed class ContentPlanFinding
{
    /// <summary>Gets or sets the machine-readable classification, when this finding supplies one.</summary>
    public string? Reason { get; set { field = value; PresentFields.Add("reason"); } }
    /// <summary>Gets or sets actionable guidance without treating it as an automatic remedy.</summary>
    public string? Resolution { get; set { field = value; PresentFields.Add("resolution"); } }
    /// <summary>Gets or sets the affected project.</summary>
    public string? ProjectId { get; set { field = value; PresentFields.Add("projectId"); } }
    /// <summary>Gets or sets the consuming project, including an explicitly null root.</summary>
    public string? RequiredBy { get; set { field = value; PresentFields.Add("requiredBy"); } }
    /// <summary>Gets or sets the requested exact release.</summary>
    public string? RequestedVersion { get; set { field = value; PresentFields.Add("requestedVersion"); } }
    /// <summary>Gets or sets the selected exact release.</summary>
    public string? SelectedVersion { get; set { field = value; PresentFields.Add("selectedVersion"); } }
    /// <summary>Gets or sets the first incompatible exact selection.</summary>
    public string? FirstVersion { get; set { field = value; PresentFields.Add("firstVersion"); } }
    /// <summary>Gets or sets the release retained by a pin policy.</summary>
    public string? PinnedVersion { get; set { field = value; PresentFields.Add("pinnedVersion"); } }
    /// <summary>Gets or sets the affected release identifier.</summary>
    public string? VersionId { get; set { field = value; PresentFields.Add("versionId"); } }
    /// <summary>Gets or sets the installed consumer retained by this plan.</summary>
    public string? RetainedConsumer { get; set { field = value; PresentFields.Add("retainedConsumer"); } }
    /// <summary>Gets or sets the retained consumer's tracked path.</summary>
    public string? ConsumerPath { get; set { field = value; PresentFields.Add("consumerPath"); } }
    /// <summary>Gets or sets the exact release required by a retained consumer.</summary>
    public string? RequiredVersion { get; set { field = value; PresentFields.Add("requiredVersion"); } }
    /// <summary>Gets or sets the local content path associated with a conflict.</summary>
    public string? Path { get; set { field = value; PresentFields.Add("path"); } }
    /// <summary>Gets or sets the provider's dependency relation name.</summary>
    public string? Relation { get; set { field = value; PresentFields.Add("relation"); } }
    /// <summary>Gets or sets the original provider dependency declaration without narrowing its fields.</summary>
    public JsonNode? Dependency { get; set { field = value; PresentFields.Add("dependency"); } }
    /// <summary>Gets or sets the unresolved loader-declared mod identifier.</summary>
    public string? DependencyId { get; set { field = value; PresentFields.Add("dependencyId"); } }
    /// <summary>Gets or sets the loader-declared requesting mod identifier.</summary>
    public string? RequestedBy { get; set { field = value; PresentFields.Add("requestedBy"); } }
    /// <summary>Gets or sets the original loader version predicate, including explicitly absent requirements.</summary>
    public JsonNode? Range { get; set { field = value; PresentFields.Add("range"); } }
    /// <summary>Gets or sets unknown fields retained when reading a saved review from another writer.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }

    [JsonIgnore]
    internal HashSet<string> PresentFields { get; } = [];
}

/// <summary>Reports an advisory local-package inspection or dependency finding without blocking launch.</summary>
public sealed class ContentAnalysisFinding
{
    /// <summary>Gets the stable finding classification.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the owning top-level package when applicable.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileName { get; init; }
    /// <summary>Gets an actual parser/access warning or the analysis limitation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }
    /// <summary>Gets the attempted nested archive path, including an explicitly missing path.</summary>
    public string? NestedPath { get; init; }
    /// <summary>Gets a duplicate mod identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    /// <summary>Gets the packages declaring the duplicate identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Files { get; init; }
    /// <summary>Gets the mod declaring a dependency or incompatibility.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Requester { get; init; }
    /// <summary>Gets the indexed required dependency while retaining its original range value.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentDependencyDeclaration? Dependency { get; init; }
    /// <summary>Gets the installed identifier named by a breaking or conflicting relation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; init; }
    /// <summary>Gets the original breaking/conflicting version predicate.</summary>
    public JsonNode? Range { get; init; }
    /// <summary>Gets whether the relation's range was fully evaluated.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RangeEvaluated { get; init; }
}