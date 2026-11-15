// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Tasks;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Runtime;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Runtime;

internal sealed record RuntimeState
{
    internal string InstanceId { get; init; } = string.Empty;
    internal string Name { get; init; } = string.Empty;
    internal RuntimeStatus? Current { get; init; }
    internal RuntimeChangeResult? Plan { get; init; }
    internal RuntimeOperationSummary[] History { get; init; } = [];
    internal string[] Installed { get; init; } = [];
    internal string[] MinecraftVersions { get; init; } = [];
    internal string[] LoaderVersions { get; init; } = [];
    internal string Minecraft { get; init; } = string.Empty;
    internal string Loader { get; init; } = "vanilla";
    internal string LoaderVersion { get; init; } = string.Empty;
    internal string Profile { get; init; } = string.Empty;
    internal bool UseInstalled { get; init; }
    internal bool Loading { get; init; }
    internal bool Active { get; init; }
    internal string Error { get; init; } = string.Empty;
    internal string CatalogSource { get; init; } = string.Empty;
    internal string TaskStatus { get; init; } = string.Empty;
    internal string TaskReport { get; init; } = string.Empty;
    internal string TaskProgress { get; init; } = string.Empty;
    internal double? Percent { get; init; }
    internal string CatalogLabel => CatalogSource switch
    {
        "network" => Resources.RuntimeSourceNetwork,
        "cache" => Resources.RuntimeSourceCache,
        "cache-fallback" => Resources.RuntimeSourceFallback,
        _ => CatalogSource
    };
    internal bool HasChanges => UseInstalled ? Profile != Current?.VersionId : Minecraft != Current?.MinecraftVersion || Loader != (Current?.Loader ?? "vanilla") || (Loader != "vanilla" && LoaderVersion != Current?.LoaderVersion);
    internal bool CanReview => !Loading && !Active && HasChanges && (UseInstalled ? Installed.Contains(Profile) : Minecraft.Length > 0 && (Loader == "vanilla" || LoaderVersion.Length > 0));
}

internal sealed class RuntimeController : IDisposable
{
    private readonly LauncherClient _client;
    private readonly HomeController _home;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly FeatureTaskController _tasks;
    private long _revision;
    private CancellationTokenSource? _rollback;

    internal RuntimeController(LauncherClient client, HomeController home)
    {
        _client = client;
        _home = home;
        _tasks = new(client, home);
        _tasks.Changed += OnTaskChanged;
    }

    internal RuntimeState State { get; private set; } = new();
    internal event Action<RuntimeState>? Changed;

    private void Publish(RuntimeState state)
    {
        if (_lifetime.IsCancellationRequested)
            return;
        State = state;
        Changed?.Invoke(state);
    }

    private void OnTaskChanged() => Publish(State with
    {
        Active = _tasks.Active,
        TaskStatus = _tasks.Status,
        TaskReport = _tasks.Report,
        TaskProgress = _tasks.Progress,
        Percent = _tasks.Job?.Progress?.OverallPercent,
        Error = _tasks.Error
    });

    internal Task CancelAsync()
    {
        if (_rollback is not null)
        {
            _rollback.Cancel();
            return Task.CompletedTask;
        }

        return _tasks.CancelAsync();
    }

    internal async Task OpenAsync(string id)
    {
        if (State.Active)
            return;
        var revision = ++_revision;
        Publish((State.InstanceId == id ? State : new RuntimeState { InstanceId = id }) with
        {
            Loading = true,
            Error = string.Empty,
            Plan = null
        });
        try
        {
            var (Name, Current, Installed, History) = await Task.Run(async () =>
                {
                    var instance = _client.Instances.Get(id);
                    var current = await _client.Runtimes.ShowAsync(id, _lifetime.Token);
                    var folder = Path.Combine(instance.MinecraftDirectory, "versions");
                    var installed = Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).Select(Path.GetFileName).OfType<string>().Where(value => File.Exists(Path.Combine(folder, value, value + ".json"))).OrderDescending(StringComparer.OrdinalIgnoreCase).ToArray() : [];
                    return (instance.Name, Current: current, Installed: installed, History: _client.Runtimes.GetOperations(id).Operations.ToArray());
                }, _lifetime.Token);
            if (revision != _revision)
                return;
            Publish(State with
            {
                Name = Name,
                Current = Current,
                Installed = Installed,
                History = History,
                Minecraft = Current.MinecraftVersion ?? string.Empty,
                Loader = Current.Loader ?? "vanilla",
                LoaderVersion = Current.LoaderVersion ?? string.Empty,
                Profile = Current.VersionId ?? string.Empty,
                Loading = false
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (revision == _revision)
            {
                Publish(State with
                {
                    Loading = false,
                    Error = exception.Message
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    internal void SetMode(int index) => Edit(state => state with { UseInstalled = index == 1 });

    internal void SetMinecraft(string value)
    {
        if (value != State.Minecraft)
        {
            Edit(state => state with
            {
                Minecraft = value,
                LoaderVersions = [],
                LoaderVersion = string.Empty,
                CatalogSource = string.Empty
            });
        }
    }

    internal void SetLoader(int index)
    {
        var loaders = new[]
        {
            "vanilla",
            "fabric",
            "quilt",
            "forge",
            "neoforge"
        };
        if (index >= 0 && index < loaders.Length && State.Loader != loaders[index])
        {
            Edit(state => state with
            {
                Loader = loaders[index],
                LoaderVersion = string.Empty,
                LoaderVersions = [],
                CatalogSource = string.Empty
            });
        }
    }

    internal void SetLoaderVersion(string value)
    {
        if (value != State.LoaderVersion)
            Edit(state => state with { LoaderVersion = value });
    }

    internal void SetProfile(int index)
    {
        if (index >= 0 && index < State.Installed.Length)
            Edit(state => state with { Profile = state.Installed[index] });
    }

    private void Edit(Func<RuntimeState, RuntimeState> edit)
    {
        if (!State.Active && !State.Loading)
        {
            Publish(edit(State) with
            {
                Plan = null,
                Error = string.Empty,
                TaskStatus = string.Empty,
                TaskReport = string.Empty
            });
        }
    }

    internal async Task LoadVersionsAsync()
    {
        if (State.Loading || State.Active)
            return;
        var revision = ++_revision;
        var minecraft = State.Minecraft;
        var loader = State.Loader;
        Publish(State with
        {
            Loading = true,
            Error = string.Empty,
            Plan = null,
            TaskStatus = string.Empty,
            TaskReport = string.Empty,
            TaskProgress = string.Empty,
            Percent = null
        });
        try
        {
            var (Minecraft, Catalog, Source) = await Task.Run(async () =>
                {
                    var versions = await _client.Installation.GetVersionsAsync(cancellationToken: _lifetime.Token);
                    var catalog = loader != "vanilla" && minecraft.Length > 0 ? await _client.Loaders.ListAsync(loader: loader, minecraftVersion: minecraft, cancellationToken: _lifetime.Token) : null;
                    return (Minecraft: versions.Versions?.Select(item => item?["id"]?.ToString()).OfType<string>().ToArray() ?? [], Catalog: catalog, Source: catalog?.Source ?? (versions.Cached ? "cache" : "network"));
                }, _lifetime.Token);
            if (revision != _revision)
                return;
            Publish(State with
            {
                MinecraftVersions = Minecraft,
                LoaderVersions = Catalog?.Versions.Select(item => item?["version"]?.ToString()).OfType<string>().ToArray() ?? [],
                CatalogSource = Source,
                Error = Catalog?.Warning ?? string.Empty,
                Loading = false
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (revision == _revision)
            {
                Publish(State with
                {
                    Loading = false,
                    Error = exception.Message
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private RuntimeChangeOptions Options(bool plan) => State.UseInstalled ? new()
    {
        VersionId = State.Profile,
        Offline = true,
        PlanOnly = plan
    }
        : new()
        {
            MinecraftVersion = State.Minecraft.Trim(),
            Loader = State.Loader == "vanilla" ? null : State.Loader,
            LoaderVersion = State.Loader == "vanilla" ? null : State.LoaderVersion.Trim(),
            RemoveLoader = State.Loader == "vanilla",
            PlanOnly = plan
        };

    internal async Task ReviewAsync()
    {
        if (!State.CanReview)
            return;
        var options = Options(true);
        Publish(State with
        {
            Loading = true,
            Error = string.Empty
        });
        try
        {
            var plan = await Task.Run(() => _client.Runtimes.ChangeAsync(State.InstanceId, options, _lifetime.Token));
            Publish(State with { Plan = plan });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(State with { Error = exception.Message });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Publish(State with { Loading = false });
        }
    }

    internal async Task ApplyAsync()
    {
        if (!State.CanReview || State.Plan is null)
            return;
        var id = State.InstanceId;
        var installed = State.UseInstalled;
        await _tasks.RunAsync(new ChangeRuntimeTask(id, Options(false)), id);
        if (_tasks.Outcome != "succeeded")
            return;
        var status = _tasks.Status;
        var report = _tasks.Report;
        await OpenAsync(id);
        Publish(State with
        {
            TaskStatus = status,
            TaskReport = report,
            UseInstalled = installed
        });
    }

    internal async Task RollbackAsync(string operationId)
    {
        if (State.Active || State.Loading)
            return;
        var id = State.InstanceId;
        if (_home.State.PendingInstances.Contains(id))
        {
            Publish(State with { Error = Resources.InstanceBusyConflict });
            return;
        }

        _home.SetManagementBusy(id, true);
        Publish(State with
        {
            Active = true,
            Error = string.Empty,
            Plan = null
        });
        _rollback = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        try
        {
            var result = await Task.Run(() => _client.Runtimes.RollbackAsync(id, operationId, _rollback.Token));
            Publish(State with { Active = false });
            await _home.RefreshAsync();
            await OpenAsync(id);
            var json = new RuntimeJsonContext(new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
            Publish(State with
            {
                TaskStatus = Resources.FeatureTaskSucceeded,
                TaskReport = JsonSerializer.Serialize(result, json.RuntimeRollbackResult)
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(State with { Error = exception.Message });
        }
        catch (OperationCanceledException)
        {
            Publish(State with { TaskStatus = Resources.FeatureTaskCanceled });
        }
        finally
        {
            _rollback.Dispose();
            _rollback = null;
            _home.SetManagementBusy(id, false);
            Publish(State with { Active = false });
        }
    }

    public void Dispose()
    {
        _tasks.Changed -= OnTaskChanged;
        _tasks.Dispose();
        _rollback?.Cancel();
        _rollback?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}