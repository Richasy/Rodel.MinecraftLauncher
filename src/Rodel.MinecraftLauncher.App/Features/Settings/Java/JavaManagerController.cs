// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Tasks;
using Rodel.MinecraftLauncher.Core.Application;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed record JavaEntry(
    string Key,
    string? Id,
    string Path,
    string Version,
    bool Usable,
    bool Managed,
    string Details);

internal sealed record JavaManagerState
{
    internal JavaEntry[] Entries { get; init; } = [];
    internal string Selected { get; init; } = string.Empty;
    internal string Source { get; init; } = string.Empty;
    internal string[] Components { get; init; } = [];
    internal string[] ComponentLabels { get; init; } = [];
    internal string?[] ComponentVersions { get; init; } = [];
    internal int Component { get; init; } = -1;
    internal string Default { get; init; } = string.Empty;
    internal bool Loading { get; init; }
    internal bool Active { get; init; }
    internal bool CancelRequested { get; init; }
    internal string Error { get; init; } = string.Empty;
    internal string Status { get; init; } = string.Empty;
    internal string Progress { get; init; } = string.Empty;
    internal JavaEntry? Entry => Entries.FirstOrDefault(entry => entry.Key == Selected);
    internal JavaEntry? DefaultEntry => Entries.FirstOrDefault(entry => entry.Id == Default || string.Equals(entry.Path, Default, StringComparison.OrdinalIgnoreCase));
    internal string DefaultLabel => Default.Length == 0
        ? Resources.JavaAutomatic
        : DefaultEntry is { } entry ? string.Format(Resources.JavaEntryTitle, entry.Version, entry.Usable ? Resources.JavaUsable : Resources.JavaUnusable) : Resources.JavaDefaultUnavailable;
    internal bool Editable => !Loading && !Active;
}

internal sealed class JavaManagerController : IDisposable
{
    private readonly LauncherClient _client;
    private readonly HomeController _home;
    private readonly CorePreferencesStore _preferences;
    private readonly FeatureTaskController _task;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _platform = "windows-" + (RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "x64"
    });

    internal JavaManagerController(LauncherClient client, HomeController home, CorePreferencesStore preferences)
    {
        _client = client;
        _home = home;
        _preferences = preferences;
        _task = new(client, home);
        _task.Changed += OnTaskChanged;
    }

    internal JavaManagerState State { get; private set; } = new();
    internal event Action<JavaManagerState>? Changed;
    internal string Outcome => _task.Outcome;

    private void Publish(JavaManagerState state)
    {
        if (!_lifetime.IsCancellationRequested)
        {
            State = state;
            Changed?.Invoke(state);
        }
    }

    private void OnTaskChanged() => Publish(State with
    {
        Active = _task.Active,
        CancelRequested = _task.CancelRequested,
        Status = _task.Status,
        Progress = _task.Progress,
        Error = _task.Error
    });

    internal void Select(string key)
    {
        if (State.Editable && key != State.Selected)
        {
            Publish(State with
            {
                Selected = key,
                Error = string.Empty
            });
        }
    }

    internal void SetSource(string source)
    {
        if (State.Editable)
            Publish(State with { Source = source });
    }

    internal void SetComponent(int component)
    {
        if (State.Editable && component != State.Component)
            Publish(State with { Component = component });
    }

    internal void BeginOperation() => Publish(State with
    {
        Error = string.Empty,
        Status = string.Empty,
        Progress = string.Empty,
        Source = string.Empty
    });

    internal async Task RefreshAsync()
    {
        if (!State.Editable)
            return;
        Publish(State with
        {
            Loading = true,
            Error = string.Empty
        });
        var warnings = new List<string>();
        var entries = new List<JavaEntry>();
        try
        {
            try
            {
                var registered = await Task.Run(() => _client.JavaRuntimes.ListAsync(_lifetime.Token));
                foreach (var item in registered)
                {
                    var id = item.Id;
                    entries.Add(new(
                            "id:" + id,
                            id,
                            item.JavaPath ?? string.Empty,
                            item.FullVersion ?? item.MajorVersion?.ToString() ?? "?",
                            item.IsUsable,
                            item.Managed == true,
                            string.Join('\n', new[] { item.Warning ?? item.VersionOutput ?? string.Empty }.Concat(item.Warnings ?? []))));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warnings.Add(exception.Message);
            }

            try
            {
                var discovered = await Task.Run(() => _client.Java.DiscoverAsync(ct: _lifetime.Token));
                foreach (var item in discovered)
                {
                    if (!entries.Any(entry => string.Equals(entry.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        entries.Add(new(
                                "path:" + item.Path,
                                null,
                                item.Path,
                                item.FullVersion ?? item.MajorVersion?.ToString() ?? "?",
                                item.IsUsable,
                                false,
                                string.Join('\n', new[] { item.VersionOutput }.Concat(item.Warnings))));
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warnings.Add(exception.Message);
            }

            var defaults = await Task.Run(() => _client.Settings.GetOverrides(), _lifetime.Token);
            Publish(State with
            {
                Entries = [.. entries],
                Default = defaults["javaRuntimeId"]?.ToString() ?? defaults["javaPath"]?.ToString() ?? string.Empty,
                Loading = false,
                Error = string.Join('\n', warnings)
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Publish(State with
            {
                Loading = false,
                Error = exception.Message
            });
        }
    }

    internal async Task LoadCatalogAsync()
    {
        if (!State.Editable)
            return;
        Publish(State with
        {
            Loading = true,
            Error = string.Empty
        });
        try
        {
            var all = await Task.Run(() => _client.JavaRuntimes.GetAvailableAsync(ct: _lifetime.Token));
            var releases = (all.GetPlatform(_platform) ?? []).Where(pair => pair.Value is JsonArray { Count: > 0 }).Select(pair =>
                {
                    var release = pair.Value!.AsArray().OrderByDescending(item => item?["version"]?["released"]?.ToString(), StringComparer.Ordinal).First();
                    var version = release?["version"]?["name"]?.ToString();
                    return new
                    {
                        Component = pair.Key,
                        Version = version,
                        Major = ParseMajor(version)
                    };
                }).ToArray();
            if (releases.All(release => release.Major is not null))
                releases = [.. releases.OrderByDescending(release => release.Major)];
            var components = releases.Select(release => release.Component).ToArray();
            var labels = releases.Select(release => "Java " + (release.Version ?? release.Component) + " · " + _platform[8..]).ToArray();
            Publish(State with
            {
                Loading = false,
                Components = components,
                ComponentLabels = labels,
                ComponentVersions = [.. releases.Select(release => release.Version)],
                Component = -1,
                Status = all.Warning ?? string.Empty,
                Error = components.Length == 0 ? Resources.JavaNoComponents : string.Empty
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Publish(State with
            {
                Loading = false,
                Error = exception.Message
            });
        }
    }

    private static int? ParseMajor(string? version)
    {
        var parts = (version ?? string.Empty).Split('.');
        var first = parts[0];
        var digits = 0;
        while (digits < first.Length && char.IsAsciiDigit(first[digits]))
            digits++;
        if (digits == 0 || (digits < first.Length && first[digits] is not ('u' or '-' or '+')) || !int.TryParse(first.AsSpan(0, digits), out var major))
            return null;
        return major == 1
            ? parts.Length > 1 && int.TryParse(parts[1], out var legacy) ? legacy : null
            : major;
    }

    internal async Task UseAsync(bool automatic = false)
    {
        if (!State.Editable)
            return;
        if (!await _preferences.FlushAsync(finishEditing: true))
            return;
        var entry = State.Entry;
        if (!automatic && entry is not { Usable: true })
            return;
        Publish(State with
        {
            Loading = true,
            Error = string.Empty
        });
        using var ownedWork = _home.TrackManagementWork();
        try
        {
            var changes = new JsonObject();
            var remove = new List<string>
            {
                "javaRuntimeExecutable"
            };
            if (automatic)
            {
                remove.AddRange(["javaRuntimeId", "javaPath"]);
            }
            else if (entry!.Id is { } id)
            {
                changes["javaRuntimeId"] = id;
                remove.Add("javaPath");
            }
            else
            {
                changes["javaPath"] = entry!.Path;
                remove.Add("javaRuntimeId");
            }

            await Task.Run(() => _client.Settings.UpdateAsync(changes, remove, cancellationToken: _lifetime.Token));
            await _preferences.LoadAsync();
            await _home.RefreshAsync();
            Publish(State with
            {
                Loading = false,
                Default = automatic ? string.Empty : entry!.Id ?? entry.Path,
                Status = Resources.JavaDefaultSaved
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Publish(State with
            {
                Loading = false,
                Error = exception.Message
            });
        }
    }

    internal async Task InstallAsync()
    {
        if (!State.Editable || State.Component < 0 || State.Component >= State.Components.Length)
            return;
        await RunAsync(new InstallJavaTask(State.Components[State.Component], _platform, State.ComponentVersions[State.Component]));
    }

    internal async Task ImportAsync()
    {
        if (!State.Editable || string.IsNullOrWhiteSpace(State.Source))
            return;
        await RunAsync(new ImportJavaTask(State.Source.Trim().Trim('"')));
    }

    internal async Task RemoveAsync(string id)
    {
        if (!State.Editable)
            return;
        var index = Array.FindIndex(State.Entries, entry => entry.Id == id);
        await RunAsync(new RemoveJavaTask(id));
        if (_task.Outcome == "succeeded" && State.Entries.Length > 0)
            Publish(State with { Selected = State.Entries[Math.Clamp(index, 0, State.Entries.Length - 1)].Key });
    }

    private async Task RunAsync(LauncherTaskRequest request)
    {
        await _task.RunAsync(request);
        var failure = State.Error;
        await RefreshAsync();
        if (_task.Outcome == "succeeded" && _task.Job?.Result?["id"]?.ToString() is { } id)
            Publish(State with { Selected = "id:" + id });
        if (failure.Length > 0)
            Publish(State with { Error = failure });
    }

    internal Task CancelAsync() => _task.CancelAsync();

    public void Dispose()
    {
        _task.Changed -= OnTaskChanged;
        _task.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}