// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>An observation of the launcher-owned embedded inference engine, never a model-load request.</summary>
public sealed record AiLocalInferenceStatus
{
    /// <summary>Gets whether the current build includes local inference support.</summary>
    public bool Available { get; init; } = AiLocalInferenceRuntime.IsSupported;

    /// <summary>Gets the requested engine ownership mode.</summary>
    public string Mode { get; init; } = "embedded";
    /// <summary>Gets whether this runtime currently owns an engine.</summary>
    public bool Initialized { get; init; }
    /// <summary>Gets the number of engines created during this runtime's lifetime.</summary>
    public long EngineInstancesCreated { get; init; }
    /// <summary>Gets the endpoint; embedded inference does not attach to a daemon.</summary>
    public string? Endpoint { get; init; }
    /// <summary>Gets whether an external service is required; always false.</summary>
    public bool ExternalServiceRequired { get; init; }
    /// <summary>Gets the native-worker ownership contract.</summary>
    public string NativeExecution { get; init; } = "library_owned_worker";
    /// <summary>Gets the initialized engine's explanation, or null before initialization.</summary>
    public string? Explanation { get; init; }
    /// <summary>Gets additional host diagnostics without changing engine ownership observations.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; set; }
}