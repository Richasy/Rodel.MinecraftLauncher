// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

public sealed class AiServiceStore(LauncherPaths paths)
{
    /// <summary>Launcher-owned local context budget when no model override is configured; never changes the SDK's global defaults.</summary>
    public const int DefaultLocalContextLength = 65536;

    private string FilePath => Path.Combine(paths.Root, "ai", "services.json");
    private AiSecretStore Secrets => new(paths);

    /// <summary>Reads service metadata and model bindings without resolving credentials or contacting a provider.</summary>
    public async Task<AiServiceConfiguration> GetAsync(CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        return Read();
    }

    /// <summary>Atomically creates or revision-checks a connection, storing secrets separately and preserving existing model bindings.</summary>
    public async Task<AiServiceConfiguration> SaveServiceAsync(AiServiceUpdate update, string? secret = null, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        var state = ReadWritable();
        var old = update.Id is null ? null : FindService(state, update.Id);
        if (old is not null)
        {
            CheckRevision(old.Revision, update.ExpectedRevision);
            if (update.Models is not null)
                throw new ArgumentException("Editing a service connection must not replace its model list. Use the model operations.");
        }
        else if (update.Models is not { Count: > 0 })
        {
            throw new ArgumentException("A new service requires at least one selected or manually entered model.");
        }

        var protocol = update.Protocol ?? old?.Protocol ?? AiProtocols.OpenAiResponses;
        if (!AiProtocols.IsKnown(protocol))
            throw new ArgumentException("Unknown AI protocol. Use openai-responses, openai-chat, anthropic-messages, gemini or rodel-inference.");
        var allowHttp = update.AllowUnencryptedHttp ?? old?.AllowUnencryptedHttp ?? false;
        if (protocol == AiProtocols.RodelInference && (!string.IsNullOrWhiteSpace(update.BaseUrl)
            || secret is not null || update.CredentialEnvironment is not null))
        {
            throw new ArgumentException("Embedded local inference does not use a service URL or API key. Use an OpenAI-compatible protocol for an independently hosted endpoint.");
        }

        var baseUrl = protocol == AiProtocols.RodelInference ? null : NormalizeEndpoint(update.BaseUrl ?? old?.BaseUrl, protocol, allowHttp);
        var credentialChanges = (secret is null ? 0 : 1) + (update.CredentialEnvironment is null ? 0 : 1) + (update.ClearCredential ? 1 : 0);
        if (credentialChanges > 1)
            throw new ArgumentException("Choose only one of a new credential, environment reference or credential removal.");
        if (update.CredentialEnvironment is { } environment && (string.IsNullOrWhiteSpace(environment) || environment.Contains('=') || environment.Any(char.IsControl)))
            throw new ArgumentException("Specify a nonempty environment variable name without '=' or control characters.");
        var id = old?.Id ?? Guid.NewGuid().ToString("N");
        var profile = (old ?? new AiServiceProfile { Id = id, DisplayName = protocol, Protocol = protocol }) with
        {
            DisplayName = update.DisplayName is null ? old?.DisplayName ?? protocol : string.IsNullOrWhiteSpace(update.DisplayName) ? protocol : update.DisplayName.Trim(),
            Protocol = protocol,
            BaseUrl = baseUrl,
            Enabled = update.Enabled ?? old?.Enabled ?? true,
            AllowUnencryptedHttp = allowHttp,
            Revision = (old?.Revision ?? 0) + 1,
            Models = old?.Models ?? [.. update.Models!.Select(model => NewModel(id, protocol, model))]
        };
        if (profile.Models.Select(model => model.Model).Distinct(StringComparer.Ordinal).Count() != profile.Models.Count)
            throw new ArgumentException("A service cannot contain duplicate model identifiers.");
        if (protocol != AiProtocols.RodelInference && profile.Models.Any(model => model.ContextLength is not null))
            throw new ArgumentException("Clear local context settings before changing this service to an online protocol.");

        string? createdSecret = null;
        var committed = false;
        try
        {
            createdSecret = secret is null ? null : Secrets.Save(secret);
            var reference = protocol == AiProtocols.RodelInference ? null
                : createdSecret ?? (update.CredentialEnvironment is { } name ? "env:" + name : update.ClearCredential ? null : old?.CredentialReference);
            profile = profile with { CredentialReference = reference };
            if (old?.CredentialReference is { } retired && retired != reference && retired.StartsWith("dpapi:", StringComparison.Ordinal))
                state.RetiredCredentials.Add(retired);
            if (old is not null)
                state.Services.Remove(old);
            state.Services.Add(profile);
            if (old is null && state.DefaultModelId is null && state.Services.Count == 1 && profile.Enabled)
                state.DefaultModelId = profile.Models[0].Id;
            await SaveAsync(state, ct).ConfigureAwait(false);
            committed = true;
            await CleanupAsync(state, ct).ConfigureAwait(false);
            return state;
        }
        finally
        {
            if (!committed && createdSecret is not null)
                Secrets.TryDelete(createdSecret);
        }
    }

    /// <summary>Removes the expected connection revision and retires unreferenced protected credentials.</summary>
    public async Task<AiServiceConfiguration> RemoveServiceAsync(string id, long expectedRevision, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        var state = ReadWritable();
        var service = FindService(state, id);
        CheckRevision(service.Revision, expectedRevision);
        state.Services.Remove(service);
        if (service.Models.Any(model => model.Id == state.DefaultModelId))
            state.DefaultModelId = null;
        if (service.CredentialReference is { } reference && reference.StartsWith("dpapi:", StringComparison.Ordinal))
            state.RetiredCredentials.Add(reference);
        await SaveAsync(state, ct).ConfigureAwait(false);
        await CleanupAsync(state, ct).ConfigureAwait(false);
        return state;
    }

    /// <summary>Adds or edits a model binding without replacing the connection or other models.</summary>
    public async Task<AiServiceConfiguration> SaveModelAsync(string serviceId, AiModelUpdate update, long expectedRevision, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        var state = ReadWritable();
        var service = FindService(state, serviceId);
        CheckRevision(service.Revision, expectedRevision);
        var old = update.Id is null ? null : service.Models.FirstOrDefault(model => model.Id == update.Id)
            ?? throw new LauncherOperationException("AI_MODEL_NOT_FOUND", "The configured model no longer exists.");
        var replacement = NewModel(serviceId, service.Protocol, update, old);
        if (service.Models.Any(model => model.Id != replacement.Id && model.Model == replacement.Model))
            throw new ArgumentException("This model identifier is already configured for the service.");
        if (old is null)
            service.Models.Add(replacement);
        else
            service.Models[service.Models.IndexOf(old)] = replacement;
        state.Services[state.Services.IndexOf(service)] = service with { Revision = service.Revision + 1 };
        await SaveAsync(state, ct).ConfigureAwait(false);
        return state;
    }

    /// <summary>Removes one model binding and clears its default selection without choosing a different model automatically.</summary>
    public async Task<AiServiceConfiguration> RemoveModelAsync(string serviceId, string modelId, long expectedRevision, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        var state = ReadWritable();
        var service = FindService(state, serviceId);
        CheckRevision(service.Revision, expectedRevision);
        var model = service.Models.FirstOrDefault(model => model.Id == modelId) ?? throw new LauncherOperationException("AI_MODEL_NOT_FOUND", "The configured model no longer exists.");
        service.Models.Remove(model);
        state.Services[state.Services.IndexOf(service)] = service with { Revision = service.Revision + 1 };
        if (state.DefaultModelId == modelId)
            state.DefaultModelId = null;
        await SaveAsync(state, ct).ConfigureAwait(false);
        return state;
    }

    /// <summary>Atomically commits an explicit model selection, preserving retained bindings and rejecting stale or duplicate selections.</summary>
    /// <param name="serviceId">The connection whose model list is being reviewed.</param>
    /// <param name="selection">Selected existing IDs and newly discovered models, captured before the first asynchronous operation.</param>
    /// <param name="expectedRevision">The service revision used to display the selection.</param>
    /// <param name="ct">Cancels the atomic persistence operation.</param>
    /// <returns>Updated configuration with stable retained model IDs and parameters.</returns>
    public async Task<AiServiceConfiguration> SelectModelsAsync(string serviceId, IReadOnlyList<AiModelUpdate> selection, long expectedRevision, CancellationToken ct = default)
    {
        var selected = selection.ToArray();
        using var lease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        var state = ReadWritable();
        var service = FindService(state, serviceId);
        CheckRevision(service.Revision, expectedRevision);
        var models = selected.Select(update => NewModel(serviceId, service.Protocol, update,
            update.Id is null ? null : service.Models.SingleOrDefault(model => model.Id == update.Id)
                ?? throw new LauncherOperationException("AI_MODEL_NOT_FOUND", "A selected model no longer belongs to this service."))).ToList();
        if (models.Select(model => model.Model).Distinct(StringComparer.Ordinal).Count() != models.Count
            || models.Select(model => model.Id).Distinct(StringComparer.Ordinal).Count() != models.Count)
        {
            throw new ArgumentException("A service cannot contain duplicate selected models.");
        }

        state.Services[state.Services.IndexOf(service)] = service with { Models = models, Revision = service.Revision + 1 };
        if (service.Models.Any(model => model.Id == state.DefaultModelId) && models.All(model => model.Id != state.DefaultModelId)) state.DefaultModelId = null;
        await SaveAsync(state, ct).ConfigureAwait(false);
        return state;
    }

    /// <summary>Changes the default for future requests after checking the settings revision and selected service state.</summary>
    public async Task<AiServiceConfiguration> SetDefaultModelAsync(string modelId, long expectedRevision, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        var state = ReadWritable();
        CheckRevision(state.Revision, expectedRevision);
        var service = state.Services.FirstOrDefault(service => service.Models.Any(model => model.Id == modelId))
            ?? throw new LauncherOperationException("AI_MODEL_NOT_FOUND", "The configured model no longer exists.");
        if (!service.Enabled)
            throw new LauncherOperationException("AI_SERVICE_DISABLED", "Enable this service before selecting its model as the default.");
        state.DefaultModelId = modelId;
        await SaveAsync(state, ct).ConfigureAwait(false);
        return state;
    }

    /// <summary>Atomically updates new-chat file, network and operation defaults without changing existing conversations or grants.</summary>
    public async Task<AiServiceConfiguration> SetAccessDefaultsAsync(bool? files, bool? network, long expectedRevision, string? mode = null, CancellationToken ct = default)
    {
        if (mode is not (null or "safe" or "allow_operations")) throw new ArgumentException("Use safe or allow_operations for the default mode.", nameof(mode));
        using var lease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        var state = ReadWritable();
        CheckRevision(state.Revision, expectedRevision);
        state.FileAccessEnabled = files ?? state.FileAccessEnabled;
        state.NetworkAccessEnabled = network ?? state.NetworkAccessEnabled;
        state.DefaultOperationMode = mode ?? state.DefaultOperationMode;
        await SaveAsync(state, ct).ConfigureAwait(false);
        return state;
    }

    internal async Task<AiServiceSnapshot> AcquireAsync(string? serviceId, string? modelId, bool requireModel, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        var state = Read();
        if (state.SchemaVersion != 1)
            throw new LauncherOperationException("AI_SETTINGS_VERSION_UNSUPPORTED", "This AI configuration can be viewed but requires a newer launcher for requests or changes.");
        if (modelId is null && serviceId is null && requireModel)
            modelId = state.DefaultModelId ?? throw new LauncherOperationException("AI_DEFAULT_MODEL_MISSING", "Select a configured model. The launcher does not choose a different service automatically.");
        var service = serviceId is null
            ? state.Services.FirstOrDefault(service => service.Models.Any(model => model.Id == modelId))
                ?? throw new LauncherOperationException("AI_MODEL_NOT_FOUND", "The configured model no longer exists.")
            : FindService(state, serviceId);
        if (!service.Enabled)
            throw new LauncherOperationException("AI_SERVICE_DISABLED", "This model service is disabled.");
        var model = modelId is null ? requireModel ? service.Models.FirstOrDefault() : null : service.Models.FirstOrDefault(model => model.Id == modelId);
        if (requireModel && model is null)
            throw new LauncherOperationException("AI_MODEL_NOT_FOUND", "Select a model belonging to this service.");
        if (service.Protocol == AiProtocols.RodelInference
            && (!string.IsNullOrWhiteSpace(service.BaseUrl) || service.CredentialReference is not null))
        {
            throw new LauncherOperationException("AI_LOCAL_CONFIGURATION_INVALID",
                "Embedded local inference does not use an endpoint or credential. Re-save this connection as local inference, or select an OpenAI-compatible protocol for an independently hosted service.");
        }

        _ = NormalizeEndpoint(service.BaseUrl, service.Protocol, service.AllowUnencryptedHttp);
        var localContext = service.Protocol == AiProtocols.RodelInference && model is not null
            ? model.ContextLength ?? (await InspectLocalModelAsync(model.Model, ct).ConfigureAwait(false)).DefaultContextLength : (int?)null;
        return new(service, model, Secrets.Open(service.Protocol == AiProtocols.RodelInference ? null : service.CredentialReference), state.FileAccessEnabled, state.NetworkAccessEnabled, localContext);
    }

    /// <summary>Reads a GGUF header off the calling thread; missing metadata uses the launcher default without loading weights.</summary>
    /// <param name="path">The selected local model file.</param>
    /// <param name="ct">Cancels before or after the bounded header read.</param>
    /// <returns>The optional model-declared length and the automatic budget capped by that declaration.</returns>
    public static async Task<AiLocalModelInfo> InspectLocalModelAsync(string path, CancellationToken ct = default)
    {
        var declared = await Task.Run(() => AiLocalModelMetadata.ReadContextLength(path), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return new(declared, Math.Min(DefaultLocalContextLength, declared ?? DefaultLocalContextLength));
    }

    /// <summary>Retries deletion of retired protected secrets once no active request lease references them.</summary>
    public async Task CleanupCredentialsAsync(CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(FilePath, ct).ConfigureAwait(false);
        var state = ReadWritable();
        await CleanupAsync(state, ct).ConfigureAwait(false);
    }

    /// <summary>Validates and normalizes an API root, preserving nonsecret query parameters and explicit unencrypted-HTTP consent.</summary>
    public static string? NormalizeEndpoint(string? value, string protocol, bool allowHttp)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return protocol switch
            {
                AiProtocols.OpenAiResponses or AiProtocols.OpenAiChat => "https://api.openai.com/v1/",
                AiProtocols.AnthropicMessages => "https://api.anthropic.com/v1/",
                AiProtocols.Gemini => "https://generativelanguage.googleapis.com/v1beta/",
                AiProtocols.RodelInference => null,
                _ => throw new ArgumentException("Unknown AI protocol.")
            };
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Use an absolute HTTP(S) API root without embedded credentials or a fragment.");
        foreach (var field in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Uri.UnescapeDataString(field.Split('=', 2)[0]);
            if (name.Contains("key", StringComparison.OrdinalIgnoreCase) || name.Contains("token", StringComparison.OrdinalIgnoreCase) || name.Contains("secret", StringComparison.OrdinalIgnoreCase) || name.Contains("password", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Keep credentials in protected storage or an environment reference, not in the API URL.");
        }

        if (uri.Scheme == "http" && !uri.IsLoopback && !allowHttp)
            throw new LauncherOperationException("AI_HTTP_CONFIRMATION_REQUIRED", "This endpoint uses unencrypted HTTP. Explicitly allow unencrypted HTTP when saving it; model data and credentials can be read on the network.");
        var builder = new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/" };
        return builder.Uri.AbsoluteUri;
    }

    private AiServiceConfiguration Read()
    {
        if (!File.Exists(FilePath))
            return new();
        try
        {
            var state = JsonFiles.ReadObject(FilePath).Deserialize(AiJsonContext.Default.AiServiceConfiguration)
                ?? throw new InvalidDataException("Expected an AI configuration object.");
            if (state.Services is null || state.RetiredCredentials is null || state.Services.Any(service => service is null || service.Models is null))
                throw new InvalidDataException("AI configuration lists must not be null.");
            return state;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            throw new LauncherOperationException("AI_SETTINGS_UNREADABLE", "The AI service configuration is malformed. It has not been replaced; repair or restore services.json.");
        }
    }

    private AiServiceConfiguration ReadWritable()
    {
        var state = Read();
        if (state.SchemaVersion != 1)
            throw new LauncherOperationException("AI_SETTINGS_VERSION_UNSUPPORTED", "This AI configuration can be viewed but requires a newer launcher for changes.");
        return state;
    }

    private Task SaveAsync(AiServiceConfiguration state, CancellationToken ct)
    {
        state.Revision++;
        return WriteAsync(state, ct);
    }

    private Task WriteAsync(AiServiceConfiguration state, CancellationToken ct)
        => JsonFiles.WriteAsync(FilePath, JsonSerializer.SerializeToNode(state, AiJsonContext.Default.AiServiceConfiguration)!, ct);

    private async Task CleanupAsync(AiServiceConfiguration state, CancellationToken ct)
    {
        var changed = false;
        foreach (var reference in state.RetiredCredentials.ToArray())
        {
            if (state.Services.Any(service => service.CredentialReference == reference))
                throw new LauncherOperationException("AI_CREDENTIAL_STATE_INVALID", "A retired credential is still referenced by a service. No credential was removed.");
            if (Secrets.TryDelete(reference))
            {
                state.RetiredCredentials.Remove(reference);
                changed = true;
            }
        }

        if (changed)
            await WriteAsync(state, ct).ConfigureAwait(false);
    }

    private static AiServiceProfile FindService(AiServiceConfiguration state, string id)
        => state.Services.FirstOrDefault(service => service.Id == id) ?? throw new LauncherOperationException("AI_SERVICE_NOT_FOUND", "The configured service no longer exists.");

    private static void CheckRevision(long actual, long? expected)
    {
        if (expected != actual)
            throw new LauncherOperationException("AI_CONFIGURATION_CONFLICT", "The configuration changed or its expected revision is missing. Read it again before applying this edit.", new JsonObject { ["expectedRevision"] = expected, ["actualRevision"] = actual });
    }

    private static AiModelBinding NewModel(string serviceId, string protocol, AiModelUpdate update, AiModelBinding? old = null)
    {
        var model = update.Model ?? old?.Model;
        if (string.IsNullOrWhiteSpace(model) || model.Length > 1024 || model.Any(char.IsControl))
            throw new ArgumentException("Specify a model, deployment or profile identifier without control characters (at most 1024 characters).");
        if (update.Temperature is { } temperature && !double.IsFinite(temperature))
            throw new ArgumentException("Temperature must be finite.");
        if (update.MaxOutputTokens is <= 0)
            throw new ArgumentException("The maximum output token count must be positive.");
        if (update.ContextLength is <= 0 || (update.ContextLength is not null && protocol != AiProtocols.RodelInference))
            throw new ArgumentException("A positive context length is a local inference load setting, not an online provider parameter.");
        return (old ?? new AiModelBinding { Id = Guid.NewGuid().ToString("N"), ServiceId = serviceId, Model = model, DisplayName = model }) with
        {
            Model = model,
            DisplayName = update.DisplayName is null ? old?.DisplayName ?? model : string.IsNullOrWhiteSpace(update.DisplayName) ? model : update.DisplayName.Trim(),
            Temperature = update.ClearParameters ? update.Temperature : update.Temperature ?? old?.Temperature,
            MaxOutputTokens = update.ClearParameters ? update.MaxOutputTokens : update.MaxOutputTokens ?? old?.MaxOutputTokens,
            ContextLength = update.ClearParameters || update.ClearContextLength ? update.ContextLength : update.ContextLength ?? old?.ContextLength
        };
    }
}

internal sealed class AiServiceSnapshot(AiServiceProfile service, AiModelBinding? model, AiCredentialLease credential,
    bool files, bool network, int? localContext = null) : IDisposable
{
    public AiServiceProfile Service { get; } = service;
    public AiModelBinding? Model { get; } = model;
    public bool FileAccessEnabled { get; } = files;
    public bool NetworkAccessEnabled { get; } = network;
    /// <summary>Gets the local model override or per-file automatic budget; online providers receive no local load setting.</summary>
    public int? EffectiveContextLength => Service.Protocol == AiProtocols.RodelInference
        ? Model?.ContextLength ?? localContext ?? AiServiceStore.DefaultLocalContextLength : null;
    [System.Text.Json.Serialization.JsonIgnore]
    public AiCredentialLease Credential { get; } = credential;
    internal AiProviderContext ProviderContext(AiLocalInferenceRuntime? localInference = null) => new(Service.Protocol, Service.Revision, Model?.Id ?? string.Empty, Model?.Model ?? string.Empty, Credential.Value, Model?.Temperature, Model?.MaxOutputTokens, localInference, EffectiveContextLength);
    internal AiServiceSnapshot Retain() => new(Service, Model, Credential.Retain(), FileAccessEnabled, NetworkAccessEnabled, EffectiveContextLength);
    /// <summary>Releases the credential lease captured with this request snapshot.</summary>
    public void Dispose() => Credential.Dispose();
}