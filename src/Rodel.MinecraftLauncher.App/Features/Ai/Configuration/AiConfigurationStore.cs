// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout.Graphics;
using Sprout.Reactive;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Owns one window's Core configuration projection without subscribing to chat events or rewriting drafts.</summary>
[Store]
internal sealed partial class AiConfigurationStore
{
    private readonly AiOperationGate _operations;
    private readonly CancellationToken _lifetime;
    private readonly Func<string?, string, CancellationToken, Task<AiModelIdentity>> _readIdentity;
    private Task<bool>? _load;
    private bool _complete;

    internal AiConfigurationStore(LauncherClient client, AiOperationGate? operations = null, Func<string?, string, CancellationToken, Task<AiModelIdentity>>? readIdentity = null)
    {
        Service = client.Assistant;
        DataRoot = client.Paths.Root;
        _operations = operations ?? new();
        _lifetime = Lifetime;
        _readIdentity = readIdentity ?? Service.GetModelIdentityAsync;
        Snapshot = new()
        {
            Artwork = new(
                [],
                new ReadOnlyDictionary<string, ImageSource>(new Dictionary<string, ImageSource>()),
                new ReadOnlyDictionary<string, ImageSource>(new Dictionary<string, ImageSource>()),
                _readIdentity,
                _lifetime)
        };
        Error = string.Empty;
    }

    internal AssistantService Service { get; }
    internal string DataRoot { get; }
    private partial AiConfigurationSnapshot Snapshot { get; set; }
    internal partial string Error { get; private set; }
    internal partial bool Loading { get; private set; }
    internal partial bool Loaded { get; private set; }
    internal string ArtworkWarning => Snapshot.Artwork.Operation.State.Error?.Message ?? string.Empty;
    internal IReadOnlyDictionary<string, ImageSource> ProviderLogos => Snapshot.Artwork.Operation.State.Value?.Providers ?? Snapshot.Artwork.PreviousProviders;
    internal IReadOnlyDictionary<string, ImageSource> ModelLogos => Snapshot.Artwork.Operation.State.Value?.Models ?? Snapshot.Artwork.PreviousModels;
    internal IReadOnlyList<AiUiProvider> Providers => Snapshot.Providers;
    internal IReadOnlyList<AiUiRegistration> Folders => Snapshot.Folders;
    internal IReadOnlyList<AiUiRegistration> Skills => Snapshot.Skills;
    internal IReadOnlyList<AiUiRegistration> Extensions => Snapshot.Extensions;
    internal IReadOnlyList<AiSkillReport> AvailableSkills => Snapshot.AvailableSkills;

    [Computed]
    private AiConversationDefaults ComputeDefaults() => Snapshot.Defaults;
    internal bool Busy => _operations.Busy;
    internal bool HasConfiguredModel => Providers.Any(provider => provider.Enabled && provider.Models.Length > 0);
    internal Task ArtworkCompletion { get; private set; } = Task.CompletedTask;

    internal Task<bool> LoadAsync(bool force = false)
    {
        if (_lifetime.IsCancellationRequested)
            return Task.FromResult(false);
        if (_load is { } pending)
            return pending;
        if (_complete && !force)
            return Task.FromResult(true);
        var completion = new TaskCompletionSource<bool>();
        _load = completion.Task;
        _ = LoadCoreAsync(completion);
        return completion.Task;
    }

    private async Task LoadCoreAsync(TaskCompletionSource<bool> completion)
    {
        try
        {
            completion.TrySetResult(await ReadConfigurationAsync());
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
        finally
        {
            _load = null;
        }
    }

    private async Task<bool> ReadConfigurationAsync()
    {
        var success = false;
        Loading = true;
        Error = string.Empty;
        try
        {
            Apply(await Task.Run(() => Service.Services.GetAsync(_lifetime), _lifetime));
            await ReadExtensionsAsync();
            _complete = true;
            success = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!_lifetime.IsCancellationRequested)
                Error = error.Message;
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
                Loading = false;
        }

        return success;
    }

    private async Task ReadExtensionsAsync()
    {
        var extensions = await Task.Run(() => Service.Extensions.GetAsync(_lifetime), _lifetime);
        var skills = await Task.Run(() => Service.ListSkillsAsync(ct: _lifetime), _lifetime);
        _lifetime.ThrowIfCancellationRequested();
        Snapshot = Snapshot with
        {
            ExtensionsRevision = extensions.Revision ?? throw new InvalidDataException(Resources.AiInvalidRecord),
            Folders = ReadEntries(extensions.FileRoots, "folder"),
            Skills = ReadEntries(extensions.SkillDirectories, "skill"),
            Extensions = ReadEntries(extensions.Mcp, "mcp"),
            AvailableSkills = skills.Items,
        };
    }

    private async Task<bool> RunAsync(Func<CancellationToken, Task> action)
    {
        if (_lifetime.IsCancellationRequested || Loading || !_operations.TryEnter())
            return false;
        Error = string.Empty;
        try
        {
            await action(_lifetime);
            return !_lifetime.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception error)
        {
            if (!_lifetime.IsCancellationRequested)
                Error = error.Message;
            return false;
        }
        finally
        {
            _operations.Exit();
        }
    }

    private void Apply(AiServiceConfiguration value)
    {
        _lifetime.ThrowIfCancellationRequested();
        var providerLogos = new Dictionary<string, ImageSource>(ProviderLogos);
        var modelLogos = new Dictionary<string, ImageSource>(ModelLogos);
        foreach (var previous in Providers)
        {
            var current = value.Services.FirstOrDefault(service => service.Id == previous.Id);
            if (current is null || previous.Endpoint != (current.BaseUrl ?? string.Empty))
                providerLogos.Remove(previous.Id);
            foreach (var model in previous.Models)
            {
                if (current is null || !current.Models.Any(binding => binding.Id == model.Id && binding.Model == model.Code))
                    modelLogos.Remove(model.Id);
            }
        }

        var providers = value.Services
            .Where(service => AiProtocols.IsLocalInferenceAvailable || service.Protocol != AiProtocols.RodelInference)
            .Select(service => new AiUiProvider(
                service.Id,
                service.DisplayName,
                service.Protocol,
                service.BaseUrl ?? string.Empty,
                [.. service.Models.Select(model => new AiUiModel(model.Id, model.DisplayName, model.Model, model.ContextLength))],
                service.Revision,
                service.Enabled,
                service.AllowUnencryptedHttp)).ToArray();
        var artwork = new AiConfigurationArtwork(
            providers,
            new ReadOnlyDictionary<string, ImageSource>(providerLogos),
            new ReadOnlyDictionary<string, ImageSource>(modelLogos),
            _readIdentity,
            _lifetime);
        Snapshot.Artwork.Operation.Cancel();
        Snapshot = Snapshot with
        {
            Providers = providers,
            Defaults = new(value.FileAccessEnabled, value.NetworkAccessEnabled, value.DefaultOperationMode == "allow_operations", value.DefaultModelId),
            Revision = value.Revision,
            Artwork = artwork,
        };
        Loaded = true;
        ArtworkCompletion = artwork.Operation.Run();
    }

    internal Task<bool> SaveServiceAsync(
        AiUiProvider? existing,
        string name,
        string protocol,
        string endpoint,
        string secret,
        AiUiModel[] models,
        bool allowHttp,
        bool enabled) => RunAsync(ct => SaveServiceCoreAsync(
            existing,
            name,
            protocol,
            endpoint,
            secret,
            models,
            allowHttp,
            enabled,
            ct));

    private async Task SaveServiceCoreAsync(
        AiUiProvider? existing,
        string name,
        string protocol,
        string endpoint,
        string secret,
        AiUiModel[] models,
        bool allowHttp,
        bool enabled,
        CancellationToken cancellation)
    {
        Apply(await Service.Services.SaveServiceAsync(new()
        {
            Id = existing?.Id,
            ExpectedRevision = existing?.Revision,
            DisplayName = name,
            Protocol = protocol,
            BaseUrl = protocol == AiProtocols.RodelInference ? null : endpoint,
            AllowUnencryptedHttp = allowHttp,
            Enabled = enabled,
            Models = existing is null ? [..models.Select(model => new AiModelUpdate
                            {
                                Model = model.Code,
                                DisplayName = model.Name
                            })] : null,
        }, string.IsNullOrEmpty(secret) || protocol == AiProtocols.RodelInference ? null : secret, cancellation));
    }

    internal Task<bool> SaveLocalModelAsync(string name, string path) => RunAsync(async ct =>
        {
            var code = path.Trim();
            var title = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(code) : name.Trim();
            var provider = Providers.FirstOrDefault(item => item.Protocol == AiProtocols.RodelInference && item.Enabled);
            if (provider is null)
            {
                await SaveServiceCoreAsync(
                    null,
                    Resources.AiLocalKind,
                    AiProtocols.RodelInference,
                    string.Empty,
                    string.Empty,
                    [new(string.Empty, title, code)],
                    false,
                    true,
                    ct);
            }
            else
            {
                await SaveModelCoreAsync(
                    provider,
                    null,
                    title,
                    code,
                    null,
                    false,
                    ct);
            }
        });

    internal Task<bool> SaveModelAsync(
        AiUiProvider provider,
        AiUiModel? model,
        string name,
        string code,
        int? contextLength = null,
        bool clearContext = false) => RunAsync(ct => SaveModelCoreAsync(
            provider,
            model,
            name,
            code,
            contextLength,
            clearContext,
            ct));

    private async Task SaveModelCoreAsync(
        AiUiProvider provider,
        AiUiModel? model,
        string name,
        string code,
        int? contextLength,
        bool clearContext,
        CancellationToken cancellation) => Apply(await Service.Services.SaveModelAsync(provider.Id, new()
        {
            Id = model?.Id,
            Model = code,
            DisplayName = name,
            ContextLength = contextLength,
            ClearContextLength = clearContext
        }, provider.Revision, cancellation));

    internal Task<bool> RemoveModelAsync(string serviceId, string modelId) => RunAsync(async ct =>
        {
            var provider = Providers.Single(item => item.Id == serviceId);
            Apply(await Service.Services.RemoveModelAsync(serviceId, modelId, provider.Revision, ct));
        });

    internal Task<bool> RemoveServiceAsync(AiUiProvider provider) => RunAsync(async ct => Apply(await Service.Services.RemoveServiceAsync(provider.Id, provider.Revision, ct)));

    internal Task<bool> DefaultsAsync(bool network, bool value) => RunAsync(async ct => Apply(await Service.Services.SetAccessDefaultsAsync(network ? null : value, network ? value : null, Snapshot.Revision, ct: ct)));

    internal Task<bool> DefaultModeAsync(bool operate) => RunAsync(async ct => Apply(await Service.Services.SetAccessDefaultsAsync(
                null,
                null,
                Snapshot.Revision,
                operate ? "allow_operations" : "safe",
                ct)));

    internal Task<bool> SaveEntryAsync(string kind, AiUiRegistration entry, bool existing) => RunAsync(async ct =>
        {
            var id = existing ? entry.Id : null;
            if (kind == "folder")
            {
                await Service.Extensions.SaveRootAsync(new()
                {
                    Id = id,
                    ExpectedRevision = Snapshot.ExtensionsRevision,
                    Path = entry.Path,
                    Mode = entry.Mode == "write" ? "read_write" : "read_only"
                }, ct);
            }
            else if (kind == "skill")
            {
                await AiExtensionStore.SaveSkillDirectoryAsync(new()
                {
                    Id = id,
                    ExpectedRevision = Snapshot.ExtensionsRevision,
                    Path = entry.Path,
                    Enabled = entry.Enabled
                }, ct);
            }
            else
            {
                await AiExtensionStore.SaveMcpAsync(new()
                {
                    Id = id,
                    ExpectedRevision = Snapshot.ExtensionsRevision,
                    DisplayName = entry.Name,
                    ExecutablePath = entry.Path,
                    WorkingDirectory = string.IsNullOrWhiteSpace(entry.WorkingDirectory) ? null : entry.WorkingDirectory,
                    Enabled = entry.Enabled,
                    Arguments = [.. Lines(entry.Arguments)],
                    Environment = Pairs(entry.Environment).ToDictionary(pair => pair.Key, pair => (string?)pair.Value),
                    CredentialEnvironment = Pairs(entry.Credential),
                }, ct);
            }

            await ReadExtensionsAsync();
        });

    internal Task<bool> RemoveEntryAsync(string kind, string id) => RunAsync(async ct =>
        {
            await Service.Extensions.RemoveAsync(Collection(kind), id, Snapshot.ExtensionsRevision, ct);
            await ReadExtensionsAsync();
        });

    internal Task<bool> EnableEntryAsync(string kind, string id, bool enabled) => RunAsync(async ct =>
        {
            await AiExtensionStore.SetEnabledAsync(
                Collection(kind),
                id,
                enabled,
                Snapshot.ExtensionsRevision,
                ct);
            await ReadExtensionsAsync();
        });

    internal async Task<AiUiModel[]> DiscoverAsync(AiUiProvider provider)
    {
        var result = await Task.Run(() => Service.ListModelsAsync(provider.Id, ct: _lifetime), _lifetime);
        return [.. result.Models.Select(model => new AiUiModel(string.Empty, model.DisplayName, model.Id))];
    }

    internal Task<bool> SelectModelsAsync(AiUiProvider provider, AiUiModel[] selected) => RunAsync(async ct => Apply(await Service.Services.SelectModelsAsync(provider.Id, [..selected.Select(model => new AiModelUpdate
                        {
                            Id = string.IsNullOrEmpty(model.Id) ? null : model.Id,
                            Model = model.Code,
                            DisplayName = model.Name
                        })], provider.Revision, ct)));

    private static string Collection(string kind) => kind == "folder"
        ? "fileRoots"
        : kind == "skill" ? "skillDirectories" : "mcp";

    private static IEnumerable<string> Lines(string value) => value.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0);

    private static Dictionary<string, string> Pairs(string value) => Lines(value).Select(line =>
        {
            var equals = line.IndexOf('=');
            if (equals <= 0)
                throw new ArgumentException(Resources.AiEnvironmentInvalid);
            return new KeyValuePair<string, string>(line[..equals].Trim(), line[(equals + 1)..]);
        }).ToDictionary();

    private static AiUiRegistration[] ReadEntries(JsonArray? source, string kind) => [..(source ?? []).OfType<JsonObject>().Where(entry => entry["id"] is not null).Select(entry =>
            {
                var path = entry[kind == "mcp" ? "executablePath" : "path"]?.ToString() ?? string.Empty;
                return new AiUiRegistration(
                    entry["id"]!.ToString(),
                    entry["displayName"]?.ToString() ?? Path.GetFileName(path),
                    path,
                    entry["enabled"] is null || (entry["enabled"] is JsonValue enabled && enabled.TryGetValue<bool>(out var value) && value),
                    entry["mode"]?.ToString() == "read_write" ? "write" : "read",
                    entry["arguments"] is JsonArray args ? string.Join('\n', args.Select(arg => arg?.ToString())) : string.Empty,
                    entry["workingDirectory"]?.ToString() ?? string.Empty,
                    FormatPairs(entry["environment"]),
                    FormatPairs(entry["credentialEnvironment"]));
            })];

    private static string FormatPairs(JsonNode? value) => value is JsonObject pairs ? string.Join('\n', pairs.Select(pair => pair.Key + "=" + pair.Value)) : string.Empty;
}

internal sealed record AiArtworkSnapshot(IReadOnlyDictionary<string, ImageSource> Providers, IReadOnlyDictionary<string, ImageSource> Models);

/// <summary>Binds optional artwork and its failure to one immutable configuration selection.</summary>
internal sealed class AiConfigurationArtwork
{
    internal AiConfigurationArtwork(
        AiUiProvider[] providers,
        IReadOnlyDictionary<string, ImageSource> previousProviders,
        IReadOnlyDictionary<string, ImageSource> previousModels,
        Func<string?, string, CancellationToken, Task<AiModelIdentity>> readIdentity,
        CancellationToken lifetime)
    {
        PreviousProviders = previousProviders;
        PreviousModels = previousModels;
        Operation = new(ct => ReadAsync(
                providers,
                previousProviders,
                previousModels,
                readIdentity,
                ct), lifetime);
    }

    internal IReadOnlyDictionary<string, ImageSource> PreviousProviders { get; }
    internal IReadOnlyDictionary<string, ImageSource> PreviousModels { get; }
    internal LatestOperation<AiArtworkSnapshot> Operation { get; }

    private static async Task<AiArtworkSnapshot> ReadAsync(
        AiUiProvider[] providers,
        IReadOnlyDictionary<string, ImageSource> previousProviders,
        IReadOnlyDictionary<string, ImageSource> previousModels,
        Func<string?, string, CancellationToken, Task<AiModelIdentity>> readIdentity,
        CancellationToken cancellation)
    {
        var providerLogos = new Dictionary<string, ImageSource>(previousProviders);
        var modelLogos = new Dictionary<string, ImageSource>(previousModels);
        foreach (var provider in providers)
        {
            if (!providerLogos.ContainsKey(provider.Id))
            {
                var identity = await Task.Run(() => readIdentity(provider.Endpoint, string.Empty, cancellation), cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (identity.ProviderId is not null && identity.LogoSvg is { } svg)
                    providerLogos[provider.Id] = AiBrandArtwork.Source(svg);
            }

            foreach (var model in provider.Models)
            {
                if (modelLogos.ContainsKey(model.Id))
                    continue;
                var identity = await Task.Run(() => readIdentity(null, model.Code, cancellation), cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (identity.LabId is not null && identity.LogoSvg is { } svg)
                    modelLogos[model.Id] = AiBrandArtwork.Source(svg);
            }
        }

        return new(new ReadOnlyDictionary<string, ImageSource>(providerLogos), new ReadOnlyDictionary<string, ImageSource>(modelLogos));
    }
}

internal sealed record AiConversationDefaults(
    bool Files = true,
    bool Network = true,
    bool Operate = false,
    string? ModelId = null);

internal sealed record AiConfigurationSnapshot
{
    internal IReadOnlyList<AiUiProvider> Providers { get; init; } = [];
    internal IReadOnlyList<AiUiRegistration> Folders { get; init; } = [];
    internal IReadOnlyList<AiUiRegistration> Skills { get; init; } = [];
    internal IReadOnlyList<AiUiRegistration> Extensions { get; init; } = [];
    internal IReadOnlyList<AiSkillReport> AvailableSkills { get; init; } = [];
    internal AiConversationDefaults Defaults { get; init; } = new();
    internal long Revision { get; init; }
    internal long ExtensionsRevision { get; init; }
    internal required AiConfigurationArtwork Artwork { get; init; }
}