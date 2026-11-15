// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Assistant;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

public static class AiProtocols
{
    /// <summary>Gets whether this build can execute local models; retained local configuration is not deleted when unavailable.</summary>
    public static bool IsLocalInferenceAvailable => AiLocalInferenceRuntime.IsSupported;

    public const string OpenAiResponses = AiProviderProtocols.OpenAiResponses;
    public const string OpenAiChat = AiProviderProtocols.OpenAiChat;
    public const string AnthropicMessages = AiProviderProtocols.AnthropicMessages;
    public const string Gemini = AiProviderProtocols.Gemini;
    public const string RodelInference = AiProviderProtocols.RodelInference;

    public static bool IsKnown(string protocol) => AiProviderProtocols.IsKnown(protocol);
}

public sealed record AiModelBinding
{
    public required string Id { get; init; }
    public required string ServiceId { get; init; }
    public required string Model { get; init; }
    public required string DisplayName { get; init; }
    public double? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    /// <summary>Gets the explicit local worker context length in tokens; null uses the launcher's local context budget.</summary>
    public int? ContextLength { get; init; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed record AiServiceProfile
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Protocol { get; init; }
    public string? BaseUrl { get; init; }
    public string? CredentialReference { get; init; }
    public bool Enabled { get; init; } = true;
    public bool AllowUnencryptedHttp { get; init; }
    public long Revision { get; init; }
    public List<AiModelBinding> Models { get; init; } = [];
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed record AiServiceConfiguration
{
    public int SchemaVersion { get; init; } = 1;
    public long Revision { get; set; }
    public List<AiServiceProfile> Services { get; init; } = [];
    public string? DefaultModelId { get; set; }
    public bool FileAccessEnabled { get; set; } = true;
    public bool NetworkAccessEnabled { get; set; } = true;
    /// <summary>Gets or sets the initial mode for new ordinary launcher chats, defaulting to approval before operations; existing and scoped conversations keep their own grants.</summary>
    public string DefaultOperationMode { get; set; } = "safe";
    public List<string> RetiredCredentials { get; init; } = [];
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A patch of connection fields. Models are supplied only for the initial, atomic service creation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiServiceUpdate
{
    public string? Id { get; init; }
    public long? ExpectedRevision { get; init; }
    public string? DisplayName { get; init; }
    public string? Protocol { get; init; }
    public string? BaseUrl { get; init; }
    public bool? Enabled { get; init; }
    public bool? AllowUnencryptedHttp { get; init; }
    public string? CredentialEnvironment { get; init; }
    public bool ClearCredential { get; init; }
    public List<AiModelUpdate>? Models { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiModelUpdate
{
    public string? Id { get; init; }
    public string? Model { get; init; }
    public string? DisplayName { get; init; }
    public double? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    /// <summary>Gets an optional local worker context length, independent of the history and output-token budgets.</summary>
    public int? ContextLength { get; init; }
    /// <summary>Resets only the local context override, preserving temperature and output settings.</summary>
    public bool ClearContextLength { get; init; }
    public bool ClearParameters { get; init; }
}

/// <summary>A foreground, single text request. It is not a persistent conversation or a tool execution.</summary>
public sealed record AiTextRequest
{
    /// <summary>Gets the saved model-binding ID; null uses the configured default or selected service model.</summary>
    public string? ModelId { get; init; }
    /// <summary>Gets an optional explicit service scope for model selection.</summary>
    public string? ServiceId { get; init; }
    /// <summary>Gets the text for a single generation without persistent conversation or tools.</summary>
    public required string Prompt { get; init; }
    /// <summary>Gets the generation deadline in seconds, independently of any terminal observer.</summary>
    public int TimeoutSeconds { get; init; } = 120;
    /// <summary>Gets a one-request temperature override; null preserves the saved model setting.</summary>
    public double? Temperature { get; init; }
    /// <summary>Gets a positive one-request output-token limit; null preserves the saved model setting.</summary>
    public int? MaximumOutputTokens { get; init; }
}

public sealed record AiHostOptions
{
    public string? ExecutablePath { get; init; }
    public IReadOnlyList<string> PrefixArguments { get; init; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, Converters = [typeof(AiUsageDetailsConverter)])]
[JsonSerializable(typeof(AiServiceConfiguration))]
[JsonSerializable(typeof(AiServiceProfile))]
[JsonSerializable(typeof(AiServiceUpdate))]
[JsonSerializable(typeof(AiModelBinding))]
[JsonSerializable(typeof(AiModelUpdate))]
[JsonSerializable(typeof(AiLocalModelInfo))]
[JsonSerializable(typeof(AiTextRequest))]
[JsonSerializable(typeof(AiSkillCatalog))]
[JsonSerializable(typeof(AiSkillReport))]
[JsonSerializable(typeof(AiSkillReport[]))]
[JsonSerializable(typeof(AiSkillReference))]
[JsonSerializable(typeof(AiExtensionReport))]
[JsonSerializable(typeof(AiChatRecord))]
[JsonSerializable(typeof(AiConversationPage))]
[JsonSerializable(typeof(AiEventPage))]
[JsonSerializable(typeof(AiChatEvent))]
[JsonSerializable(typeof(AiHostStatus))]
[JsonSerializable(typeof(AiLocalModelCatalog))]
[JsonSerializable(typeof(AiLocalInferenceStatus))]
[JsonSerializable(typeof(AiContextPage))]
[JsonSerializable(typeof(AiConfirmationPage))]
[JsonSerializable(typeof(AiIntegrationPlan))]
[JsonSerializable(typeof(AiIntegrationExport))]
[JsonSerializable(typeof(AiIntegrationReceipt))]
[JsonSerializable(typeof(AiIntegrationFileOutcome[]))]
[JsonSerializable(typeof(AiIntegrationMcpOutcome))]
[JsonSerializable(typeof(AiMcpCatalog))]
[JsonSerializable(typeof(AiMcpDescription))]
[JsonSerializable(typeof(AiConversationRemovalReview))]
[JsonSerializable(typeof(AiTurnCatalog))]
[JsonSerializable(typeof(AiToolConfirmationReceipt))]
[JsonSerializable(typeof(AiConversationRemovalReceipt))]
[JsonSerializable(typeof(AiConversationExportReceipt))]
[JsonSerializable(typeof(AiUserAnswer))]
[JsonSerializable(typeof(AiModelCatalogReport))]
[JsonSerializable(typeof(AiHttpModelCatalogReport))]
[JsonSerializable(typeof(AiServiceModelCatalogReport))]
[JsonSerializable(typeof(AiHttpServiceModelCatalogReport))]
[JsonSerializable(typeof(AiGenerationReport))]
[JsonSerializable(typeof(AiHttpGenerationReport))]
[JsonSerializable(typeof(string))]
public sealed partial class AiJsonContext : JsonSerializerContext;