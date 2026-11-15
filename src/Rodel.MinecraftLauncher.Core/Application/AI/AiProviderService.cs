// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiProviderService(LauncherPaths paths, AiLocalInferenceRuntime localInference)
{
    /// <summary>Lists actual provider/store models without changing the configured model bindings.</summary>
    public async Task<AiServiceModelCatalogReport> ListModelsAsync(string serviceId, CancellationToken ct)
    {
        var (Result, Service, BindingId, Model, _) = await ExecuteAsync(serviceId, null, null, 30, null, ct).ConfigureAwait(false);
        return AiProviderReports.Catalog((AiModelCatalogResult)Result, Service, BindingId, Model);
    }

    /// <summary>Generates with a captured connection and optional temporary parameters, never persisting request overrides.</summary>
    public async Task<AiGenerationReport> GenerateAsync(AiTextRequest request, CancellationToken ct)
    {
        AssistantService.ValidateRequest(request);
        var (Result, Service, BindingId, Model, Parameters) = await ExecuteAsync(request.ServiceId, request.ModelId, request.Prompt, request.TimeoutSeconds, request, ct).ConfigureAwait(false);
        return AiProviderReports.Generation((AiTextResult)Result, Service, BindingId, Model, Parameters);
    }

    private async Task<(AiProviderResult Result, AiProviderConnectionReport Service, string? BindingId, string? Model, AiEffectiveParameters Parameters)> ExecuteAsync(
        string? serviceId, string? modelId, string? prompt, int timeout, AiTextRequest? request, CancellationToken ct)
    {
        using var snapshot = await new AiServiceStore(paths).AcquireAsync(serviceId, modelId, prompt is not null, ct).ConfigureAwait(false);
        var service = snapshot.Service;
        try
        {
            var endpoint = service.Protocol == AiProtocols.RodelInference ? null : new Uri(service.BaseUrl!);
            if (endpoint is not null) _ = AiServiceStore.NormalizeEndpoint(endpoint.AbsoluteUri, service.Protocol, service.AllowUnencryptedHttp);
            var context = snapshot.ProviderContext(localInference) with
            {
                Temperature = request?.Temperature ?? snapshot.Model?.Temperature,
                MaxOutputTokens = request?.MaximumOutputTokens ?? snapshot.Model?.MaxOutputTokens
            };
            if (prompt is not null && context.MaxOutputTokens is null && endpoint is not null)
            {
                using var capacity = new AiContextBudget(paths, snapshot, endpoint);
                context = context with { MaxOutputTokens = await capacity.GetOutputLimitAsync(ct).ConfigureAwait(false) };
            }

            var providerResult = await AiProviderRequests.ExecuteAsync(context, endpoint, prompt, timeout, ct).ConfigureAwait(false);
            var connection = new AiProviderConnectionReport
            {
                Id = service.Id,
                DisplayName = service.DisplayName,
                Protocol = service.Protocol,
                Revision = service.Revision,
                Endpoint = endpoint is null ? null : new UriBuilder(endpoint) { Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri,
                Hosting = endpoint is null ? "embedded" : "remote"
            };
            return (providerResult, connection, snapshot.Model?.Id, snapshot.Model?.Model,
                new(context.Temperature, context.MaxOutputTokens, context.ContextLength));
        }
        catch (AiProtocolException error)
        {
            throw new LauncherOperationException(error.Code, error.Message, error.Details);
        }
    }
}