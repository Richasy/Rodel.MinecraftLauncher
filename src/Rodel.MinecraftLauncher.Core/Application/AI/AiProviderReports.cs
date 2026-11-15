// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Assistant;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>A provider model reference discovered without changing a saved binding.</summary>
/// <param name="Id">The provider's model identifier.</param>
/// <param name="DisplayName">The provider's label, or its identifier when no label was supplied.</param>
public sealed record AiDiscoveredModel(string Id, string DisplayName);

/// <summary>A bounded model discovery result; discovery is not a generation capability test.</summary>
[JsonDerivedType(typeof(AiHttpModelCatalogReport))]
[JsonDerivedType(typeof(AiServiceModelCatalogReport))]
[JsonDerivedType(typeof(AiHttpServiceModelCatalogReport))]
public record AiModelCatalogReport
{
    /// <summary>Gets discovered models in provider order after provider-layer deduplication.</summary>
    public required AiDiscoveredModel[] Models { get; init; }
    /// <summary>Gets the duration of the provider operation.</summary>
    public long ElapsedMilliseconds { get; init; }
    /// <summary>Gets the offered tool count; discovery offers none.</summary>
    public int ToolsOffered { get; init; }
    /// <summary>Gets whether this request retained a conversation; always false.</summary>
    public bool PersistentConversation { get; init; }
    /// <summary>Gets whether discovery preserved configured bindings; always true.</summary>
    public bool ConfiguredModelsUnchanged { get; init; } = true;
    /// <summary>Gets whether generation was tested; always false.</summary>
    public bool GenerationTested { get; init; }
    /// <summary>Gets unfamiliar host response fields without discarding their original JSON values.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>An online discovery result with the provider's sanitized request correlation.</summary>
public sealed record AiHttpModelCatalogReport : AiModelCatalogReport
{
    /// <summary>Gets the sanitized HTTP request identifier, including explicit null when none was supplied.</summary>
    public string? RequestId { get; init; }
}

/// <summary>A catalog read through one captured, configured Core connection.</summary>
[JsonDerivedType(typeof(AiHttpServiceModelCatalogReport))]
public record AiServiceModelCatalogReport : AiModelCatalogReport
{
    /// <summary>Gets the captured public connection identity without its credential.</summary>
    public required AiProviderConnectionReport Service { get; init; }
    /// <summary>Gets the selected saved model binding, if any.</summary>
    public string? ModelBindingId { get; init; }
    /// <summary>Gets the requested provider model, if any.</summary>
    public string? RequestedModel { get; init; }
}

/// <summary>An online configured catalog with HTTP correlation, distinct from Embedded model discovery.</summary>
public sealed record AiHttpServiceModelCatalogReport : AiServiceModelCatalogReport
{
    /// <summary>Gets the sanitized HTTP request identifier, including explicit null when absent.</summary>
    public string? RequestId { get; init; }
}

/// <summary>The connection identity actually captured for a bounded provider operation.</summary>
public sealed record AiProviderConnectionReport
{
    /// <summary>Gets the saved service identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets its captured display name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Gets the configured provider protocol.</summary>
    public required string Protocol { get; init; }
    /// <summary>Gets the captured service revision.</summary>
    public long Revision { get; init; }
    /// <summary>Gets the endpoint with query and fragment removed, or null for Embedded inference.</summary>
    public string? Endpoint { get; init; }
    /// <summary>Gets embedded or remote.</summary>
    public required string Hosting { get; init; }
}

/// <summary>The generation parameters actually used, independently of saved model defaults.</summary>
/// <param name="Temperature">The effective temperature override or saved value.</param>
/// <param name="MaxOutputTokens">The effective bounded output budget.</param>
/// <param name="ContextLength">The local context budget, when applicable.</param>
public sealed record AiEffectiveParameters(double? Temperature, int? MaxOutputTokens, int? ContextLength);

/// <summary>A foreground generation outcome with Core connection identity and native MEAI token accounting.</summary>
[JsonDerivedType(typeof(AiHttpGenerationReport))]
public record AiGenerationReport
{
    /// <summary>Gets completed, incomplete or refused.</summary>
    public required string Status { get; init; }
    /// <summary>Gets the provider's visible answer.</summary>
    public required string Text { get; init; }
    /// <summary>Gets the provider's terminal reason without inferring tool effects.</summary>
    public string? FinishReason { get; init; }
    /// <summary>Gets the actual provider response identifier.</summary>
    public string? ResponseId { get; init; }
    /// <summary>Gets the actual model reported by the provider.</summary>
    public string? Model { get; init; }
    /// <summary>Gets provider-reported MEAI usage, without estimating missing values.</summary>
    public UsageDetails? Usage { get; init; }
    /// <summary>Gets the provider operation duration.</summary>
    public long ElapsedMilliseconds { get; init; }
    /// <summary>Gets the number of tools offered; isolated generation offers none.</summary>
    public int ToolsOffered { get; init; }
    /// <summary>Gets whether a conversation was retained; always false.</summary>
    public bool PersistentConversation { get; init; }
    /// <summary>Gets the captured connection identity without its credential.</summary>
    public required AiProviderConnectionReport Service { get; init; }
    /// <summary>Gets the selected saved model binding.</summary>
    public string? ModelBindingId { get; init; }
    /// <summary>Gets the requested provider model identifier.</summary>
    public string? RequestedModel { get; init; }
    /// <summary>Gets the actual request parameters rather than modifying saved defaults.</summary>
    public required AiEffectiveParameters EffectiveParameters { get; init; }
    /// <summary>Gets unfamiliar host response fields without interpreting them as model or tool evidence.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>An online generation outcome retaining HTTP-only refusal and request-correlation fields.</summary>
public sealed record AiHttpGenerationReport : AiGenerationReport
{
    /// <summary>Gets the provider refusal text or block reason.</summary>
    public string? Refusal { get; init; }
    /// <summary>Gets the sanitized HTTP request identifier.</summary>
    public string? RequestId { get; init; }
}

internal static class AiProviderReports
{
    internal static AiModelCatalogReport Discovery(AiModelCatalogResult result)
    {
        var report = result is AiHttpModelCatalogResult http
            ? new AiHttpModelCatalogReport { Models = Models(result), RequestId = http.RequestId }
            : new AiModelCatalogReport { Models = Models(result) };
        return report with { ElapsedMilliseconds = result.ElapsedMilliseconds };
    }

    internal static AiServiceModelCatalogReport Catalog(AiModelCatalogResult result, AiProviderConnectionReport service, string? bindingId, string? model)
    {
        var report = result is AiHttpModelCatalogResult http
            ? new AiHttpServiceModelCatalogReport { Models = Models(result), Service = service, RequestId = http.RequestId }
            : new AiServiceModelCatalogReport { Models = Models(result), Service = service };
        return report with { ElapsedMilliseconds = result.ElapsedMilliseconds, ModelBindingId = bindingId, RequestedModel = model };
    }

    internal static AiGenerationReport Generation(AiTextResult result, AiProviderConnectionReport service, string? bindingId, string? model, AiEffectiveParameters parameters)
    {
        var report = result is AiHttpTextResult http
            ? new AiHttpGenerationReport { Status = result.Status, Text = result.Text, Service = service, EffectiveParameters = parameters, Refusal = http.Refusal, RequestId = http.RequestId }
            : new AiGenerationReport { Status = result.Status, Text = result.Text, Service = service, EffectiveParameters = parameters };
        return report with
        {
            FinishReason = result.FinishReason,
            ResponseId = result.ResponseId,
            Model = result.Model,
            Usage = result.Usage,
            ElapsedMilliseconds = result.ElapsedMilliseconds,
            ModelBindingId = bindingId,
            RequestedModel = model
        };
    }

    internal static AiServiceModelCatalogReport ReadCatalog(JsonObject response) => response.ContainsKey("requestId")
        ? response.Deserialize(AiJsonContext.Default.AiHttpServiceModelCatalogReport) ?? throw new JsonException("Empty provider catalog.")
        : response.Deserialize(AiJsonContext.Default.AiServiceModelCatalogReport) ?? throw new JsonException("Empty provider catalog.");

    internal static AiGenerationReport ReadGeneration(JsonObject response) => response.ContainsKey("requestId")
        ? response.Deserialize(AiJsonContext.Default.AiHttpGenerationReport) ?? throw new JsonException("Empty generation result.")
        : response.Deserialize(AiJsonContext.Default.AiGenerationReport) ?? throw new JsonException("Empty generation result.");

    private static AiDiscoveredModel[] Models(AiModelCatalogResult result)
        => [.. result.Models.Select(model => new AiDiscoveredModel(model.Id, model.DisplayName))];
}