// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.Assistant.AssistantCommandFactory;
using C = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;
using O = Rodel.MinecraftLauncher.Cli.Assistant.AssistantOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.AssistantStrings;

namespace Rodel.MinecraftLauncher.Cli.Assistant;

internal static class ServiceCommands
{
    /// <summary>Registers service/model configuration and shared foreground or Embedded inference operations.</summary>
    public static void Register(Command root)
    {
        Add(root, "host", S.Host, async (app, p, ct) => await app.Client.GetHostAsync(app.Host, ct).ConfigureAwait(false));
        var local = Group(root, "local", S.LocalInference);
        Add(local, "status", S.Status, async (app, p, ct) => await app.Client.GetLocalInferenceStatusAsync(app.Host, ct).ConfigureAwait(false));
        Add(local, "inspect", C.InspectLocalModelDescription, async (app, p, ct) => JsonSerializer.SerializeToNode(await AiServiceStore.InspectLocalModelAsync(AssistantInput.Required(p, O.Path), ct).ConfigureAwait(false), AiJsonContext.Default.AiLocalModelInfo), options: [O.Path]);
        Add(local, "models", S.Models, async (app, p, ct) => await app.Client.ListLocalModelsAsync(app.Host, ct).ConfigureAwait(false));
        Add(local, "unload", S.UnloadLocalInference, async (app, p, ct) => await app.Client.UnloadLocalInferenceAsync(app.Host, ct).ConfigureAwait(false));
        Add(root, "request", S.Request, async (app, p, ct) => await app.Client.GenerateAsync(await RequestAsync(app, p, p.GetValue(O.Service), ct).ConfigureAwait(false), app.Host, ct).ConfigureAwait(false),
            options: [O.Service, O.Models, O.Prompt, O.PromptFile, O.PromptStdin, O.Timeout, O.Temperature, O.OutputTokens], execution: true);
        var service = Group(root, "service", S.Service);
        Add(service, "list", S.List, async (app, p, ct) => State(await app.Client.Services.GetAsync(ct).ConfigureAwait(false)));
        Add(service, "show", S.Show, async (app, p, ct) =>
        {
            var id = p.GetRequiredValue(O.Target);
            var value = (await app.Client.Services.GetAsync(ct).ConfigureAwait(false)).Services.FirstOrDefault(item => item.Id == id)
                ?? throw new LauncherOperationException("AI_SERVICE_NOT_FOUND", S.ServiceNotFound);
            return JsonSerializer.SerializeToNode(value, AiJsonContext.Default.AiServiceProfile);
        }, [O.Target]);
        Add(service, "save", S.Save, SaveServiceAsync, options: [O.File, O.Id, O.Revision, O.Name, O.Protocol, O.BaseUrl, O.Enabled, O.AllowHttp, O.Models, O.CredentialStdin, O.CredentialPrompt, O.CredentialEnvironment, O.ClearCredential]);
        Add(service, "remove", S.Remove, async (app, p, ct) => State(await app.Client.Services.RemoveServiceAsync(p.GetRequiredValue(O.Target), AssistantInput.Revision(p), ct).ConfigureAwait(false)), [O.Target], [O.Revision]);
        Add(service, "models", S.Models, async (app, p, ct) => await app.Client.ListModelsAsync(p.GetRequiredValue(O.Target), app.Host, ct).ConfigureAwait(false), [O.Target]);
        var model = Group(root, "model", S.Model);
        Add(model, "default", S.Default, async (app, p, ct) => State(await app.Client.Services.SetDefaultModelAsync(p.GetRequiredValue(O.Target), AssistantInput.Revision(p), ct).ConfigureAwait(false)), [O.Target], [O.Revision]);
        Add(model, "save", S.Save, SaveModelAsync, options: [O.Service, O.File, O.Revision, O.Id, O.Models, O.Name, O.Temperature, O.OutputTokens, O.ContextLength, O.ClearContextLength, O.ClearParameters]);
        Add(model, "remove", S.Remove, async (app, p, ct) => State(await app.Client.Services.RemoveModelAsync(AssistantInput.Required(p, O.Service), p.GetRequiredValue(O.Target), AssistantInput.Revision(p), ct).ConfigureAwait(false)),
            [O.Target], [O.Service, O.Revision]);
        var access = Group(root, "access", S.Access);
        Add(access, "show", S.Show, async (app, p, ct) => Access(await app.Client.Services.GetAsync(ct).ConfigureAwait(false)));
        Add(access, "set", S.Save, async (app, p, ct) => Access(await app.Client.Services.SetAccessDefaultsAsync(p.GetValue(O.Files), p.GetValue(O.Network), AssistantInput.Revision(p), p.GetValue(O.Mode), ct).ConfigureAwait(false)),
            options: [O.Files, O.Network, O.Mode, O.Revision]);
    }
    private static async Task<JsonNode?> SaveServiceAsync(AssistantInvocation app, ParseResult parsed, CancellationToken ct)
    {
        var request = parsed.GetValue(O.File) is { } file
            ? await AssistantInput.JsonAsync(file, AiJsonContext.Default.AiServiceUpdate, ct).ConfigureAwait(false)
            : new AiServiceUpdate
            {
                Id = parsed.GetValue(O.Id),
                ExpectedRevision = parsed.GetValue(O.Revision),
                DisplayName = parsed.GetValue(O.Name),
                Protocol = parsed.GetValue(O.Protocol),
                BaseUrl = parsed.GetValue(O.BaseUrl),
                Enabled = parsed.GetValue(O.Enabled),
                AllowUnencryptedHttp = parsed.GetValue(O.AllowHttp),
                Models = parsed.GetValue(O.Models) is { Length: > 0 } models ? [.. models.Select(model => new AiModelUpdate { Model = model })] : null
            };
        request = request with { CredentialEnvironment = parsed.GetValue(O.CredentialEnvironment) ?? request.CredentialEnvironment, ClearCredential = parsed.GetValue(O.ClearCredential) || request.ClearCredential };
        return State(await app.Client.Services.SaveServiceAsync(request, await AssistantInput.CredentialAsync(parsed, app.AllowsSecretInput, ct).ConfigureAwait(false), ct).ConfigureAwait(false));
    }
    private static async Task<JsonNode?> SaveModelAsync(AssistantInvocation app, ParseResult parsed, CancellationToken ct)
    {
        var request = parsed.GetValue(O.File) is { } file
            ? await AssistantInput.JsonAsync(file, AiJsonContext.Default.AiModelUpdate, ct).ConfigureAwait(false)
            : new AiModelUpdate { Id = parsed.GetValue(O.Id), Model = AssistantInput.Model(parsed), DisplayName = parsed.GetValue(O.Name), Temperature = parsed.GetValue(O.Temperature), MaxOutputTokens = parsed.GetValue(O.OutputTokens), ContextLength = parsed.GetValue(O.ContextLength), ClearContextLength = parsed.GetValue(O.ClearContextLength), ClearParameters = parsed.GetValue(O.ClearParameters) };
        return State(await app.Client.Services.SaveModelAsync(AssistantInput.Required(parsed, O.Service), request, AssistantInput.Revision(parsed), ct).ConfigureAwait(false));
    }
    private static async Task<AiTextRequest> RequestAsync(AssistantInvocation app, ParseResult parsed, string? service, CancellationToken ct)
        => new()
        {
            ServiceId = service,
            ModelId = AssistantInput.Model(parsed),
            TimeoutSeconds = parsed.GetValue(O.Timeout) ?? 120,
            Temperature = parsed.GetValue(O.Temperature),
            MaximumOutputTokens = parsed.GetValue(O.OutputTokens),
            Prompt = await AssistantInput.PromptAsync(parsed, app.AllowsSecretInput, null, ct).ConfigureAwait(false)
        };
    private static JsonNode State(AiServiceConfiguration configuration) => JsonSerializer.SerializeToNode(configuration, AiJsonContext.Default.AiServiceConfiguration)!;
    private static JsonObject Access(AiServiceConfiguration configuration) => new()
    {
        ["revision"] = configuration.Revision,
        ["fileAccessEnabled"] = configuration.FileAccessEnabled,
        ["networkAccessEnabled"] = configuration.NetworkAccessEnabled,
        ["defaultOperationMode"] = configuration.DefaultOperationMode,
        ["capabilityExecutionImplemented"] = false
    };
}