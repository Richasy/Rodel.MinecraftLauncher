// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Sprout.Reactive;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed record CorePreferencesState
{
    internal JsonObject Stored { get; init; } = [];
    internal IReadOnlyDictionary<string, string> Changes { get; init; } = new Dictionary<string, string>();
    internal IReadOnlySet<string> Removed { get; init; } = new HashSet<string>();
    internal IReadOnlySet<string> Editing { get; init; } = new HashSet<string>();
    internal bool Busy { get; init; }
    internal bool Loaded { get; init; }
    internal bool ProxyManual { get; init; }
    internal string Error { get; init; } = string.Empty;
    internal string SavedCategory { get; init; } = string.Empty;

    internal JsonNode? StoredValue(string key) => key.StartsWith("network.", StringComparison.Ordinal) ? (Stored["network"] as JsonObject)?[key[8..]] : Stored[key];

    internal string Text(PreferenceField field)
    {
        if (Removed.Contains(field.Key))
            return field.Default;
        if (Changes.TryGetValue(field.Key, out var value))
            return value;
        var stored = StoredValue(field.Key);
        if (stored is null)
            return field.Default;
        if (field.Kind == PreferenceKind.Arguments && stored is JsonArray args)
            return string.Join('\n', args.Select(item => item?.ToString() ?? string.Empty));
        if (field.Kind is PreferenceKind.Environment or PreferenceKind.Mirrors && stored is JsonObject pairs)
            return string.Join('\n', pairs.Select(pair => pair.Key + "=" + pair.Value?.ToString()));
        return stored.ToString();
    }

    internal bool HasChanges(string category) => CorePreferenceFields.For(category).Any(field => Changes.ContainsKey(field.Key) || Removed.Contains(field.Key));

    internal bool CanReset(string category) => HasChanges(category) || CorePreferenceFields.For(category).Any(field => StoredValue(field.Key) is not null) || (category == "java" && StoredValue("javaRuntimeId") is not null);
}

/// <summary>Owns editable overrides and their existing merge/flush protocol independently of settings controls.</summary>
[Store]
internal sealed partial class CorePreferencesStore : IAsyncDisposable
{
    private readonly LauncherClient _client;
    private readonly Action _saved;
    private readonly Func<IDisposable> _trackWork;
    private readonly Func<string, JsonObject, IReadOnlyList<string>, CancellationToken, Task> _write;
    private readonly CancellationToken _lifetime;
    private CancellationTokenSource? _pendingSave;
    private Task? _disposal;
    private bool _autoSave;
    private string _proxyDraft = string.Empty;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    internal CorePreferencesStore(
        LauncherClient client,
        Action saved,
        Func<IDisposable> trackWork,
        Func<string, JsonObject, IReadOnlyList<string>, CancellationToken, Task>? write = null)
    {
        _client = client;
        _saved = saved;
        _trackWork = trackWork;
        _lifetime = Lifetime;
        _write = write ?? ((
            category,
            changes,
            removed,
            ct) => category == "network" ? client.Settings.UpdateSectionAsync("network", changes, removed, cancellationToken: ct) : client.Settings.UpdateAsync(changes, removed, cancellationToken: ct));
        State = new();
    }

    internal void EnableAutoSave() => _autoSave = true;
    internal bool HasPendingWrites => State.Busy || CorePreferenceFields.All.Select(preference => preference.Category).Distinct().Any(State.HasChanges);

    private void ScheduleSave()
    {
        if (!_autoSave || _lifetime.IsCancellationRequested)
            return;
        _pendingSave?.Cancel();
        _pendingSave = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _ = SaveAfterDelayAsync(_pendingSave);
    }

    private async Task SaveAfterDelayAsync(CancellationTokenSource pending)
    {
        try
        {
            await Task.Delay(500, pending.Token);
            await FlushAsync();
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_pendingSave, pending))
                _pendingSave = null;
            pending.Dispose();
        }
    }

    internal async Task<bool> FlushAsync(bool finishEditing = false)
    {
        if (finishEditing)
            Publish(State with { Editing = new HashSet<string>() });
        await _saveGate.WaitAsync(_lifetime);
        try
        {
            bool wrote;
            do
            {
                wrote = false;
                foreach (var category in CorePreferenceFields.All.Select(field => field.Category).Distinct())
                {
                    if (!State.HasChanges(category) || CorePreferenceFields.For(category).Any(field => State.Editing.Contains(field.Key)))
                    {
                        continue;
                    }

                    wrote |= await SaveAsync(category, validOnly: true);
                    if (State.Error.Length > 0)
                        return false;
                }
            }
            while (wrote);
            return State.Error.Length == 0 && !_lifetime.IsCancellationRequested;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    internal partial CorePreferencesState State { get; private set; }

    private void Publish(CorePreferencesState state)
    {
        if (!_lifetime.IsCancellationRequested)
            State = state;
    }

    internal async Task LoadAsync()
    {
        if (State.Busy)
            return;
        Publish(State with
        {
            Busy = true,
            Error = string.Empty
        });
        try
        {
            var stored = await Task.Run(() => _client.Settings.GetOverrides(), _lifetime);
            var storedProxy = (stored["network"] as JsonObject)?["proxy"]?.ToString();
            if (!State.Loaded)
                _proxyDraft = storedProxy is { Length: > 0 } and not "direct" ? storedProxy : string.Empty;
            Publish(State with
            {
                Stored = stored,
                Loaded = true,
                Busy = false,
                ProxyManual = stored["network"] is JsonObject network && network["proxy"]?.ToString() is { Length: > 0 } proxy && proxy != "direct"
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Publish(State with
            {
                Busy = false,
                Error = exception.Message
            });
        }
    }

    internal void Set(PreferenceField field, string value)
    {
        if (!State.Loaded || State.Text(field) == value)
            return;
        if (field.Kind == PreferenceKind.Proxy && State.ProxyManual)
            _proxyDraft = value;
        var changes = new Dictionary<string, string>(State.Changes)
        {
            [field.Key] = value
        };
        var removed = State.Removed.ToHashSet();
        removed.Remove(field.Key);
        var unchanged = State with
        {
            Changes = new Dictionary<string, string>(),
            Removed = new HashSet<string>()
        };
        if (!State.Busy && unchanged.Text(field) == value)
            changes.Remove(field.Key);
        Publish(State with
        {
            Changes = changes,
            Removed = removed,
            Error = string.Empty,
            SavedCategory = string.Empty
        });
        ScheduleSave();
    }

    internal void SetNumber(PreferenceField field, double value) => Set(field, double.IsNaN(value) ? string.Empty : value.ToString("R", CultureInfo.InvariantCulture));

    internal void SetProxyMode(int index)
    {
        var field = CorePreferenceFields.All.First(value => value.Kind == PreferenceKind.Proxy);
        if (!State.Loaded)
            return;
        if (State.ProxyManual)
            _proxyDraft = State.Text(field);
        var value = index == 1
            ? "direct"
            : index == 0 ? string.Empty : _proxyDraft;
        // Validate the address change against the newly requested mode.
        Publish(State with
        {
            ProxyManual = index == 2,
            SavedCategory = string.Empty
        });
        Set(field, value);
        ScheduleSave();
    }

    internal void SetEditing(string key, bool editing)
    {
        var fields = State.Editing.ToHashSet();
        if (editing)
            fields.Add(key);
        else
            fields.Remove(key);
        Publish(State with { Editing = fields });
        if (!editing)
            ScheduleSave();
    }

    internal void Reset(string category)
    {
        if (State.Busy || !State.Loaded)
            return;
        var fields = CorePreferenceFields.For(category);
        var changes = State.Changes.Where(pair => !fields.Any(field => field.Key == pair.Key)).ToDictionary();
        var removed = State.Removed.ToHashSet();
        foreach (var field in fields)
        {
            if (State.StoredValue(field.Key) is not null || (field.Key == "javaPath" && State.StoredValue("javaRuntimeId") is not null))
                removed.Add(field.Key);
        }

        if (category == "network")
            _proxyDraft = string.Empty;
        Publish(State with
        {
            Changes = changes,
            Removed = removed,
            ProxyManual = category != "network" && State.ProxyManual,
            Error = string.Empty,
            SavedCategory = string.Empty
        });
        ScheduleSave();
    }

    internal void Discard(string category)
    {
        if (State.Busy)
            return;
        var keys = CorePreferenceFields.For(category).Select(field => field.Key).ToHashSet();
        var next = State with
        {
            Changes = State.Changes.Where(pair => !keys.Contains(pair.Key)).ToDictionary(),
            Removed = State.Removed.Where(key => !keys.Contains(key)).ToHashSet(),
            Error = string.Empty,
            SavedCategory = string.Empty
        };
        if (category == "network")
        {
            next = next with
            {
                ProxyManual = next.StoredValue("network.proxy")?.ToString() is { Length: > 0 } proxy && proxy != "direct"
            };
        }

        if (category == "network")
            _proxyDraft = next.ProxyManual ? next.Text(CorePreferenceFields.All.First(field => field.Kind == PreferenceKind.Proxy)) : string.Empty;
        Publish(next);
    }

    internal string ProxyValidation
    {
        get
        {
            if (!State.ProxyManual)
                return string.Empty;
            var preference = CorePreferenceFields.All.First(item => item.Kind == PreferenceKind.Proxy);
            var value = State.Text(preference);
            if (string.IsNullOrWhiteSpace(value) || value == "direct")
                return Resources.SettingsProxyRequired;
            try
            {
                _ = Parse(preference, value);
                return string.Empty;
            }
            catch (ArgumentException exception)
            {
                return exception.Message;
            }
        }
    }

    internal string Validation(string category, bool includeProxy = true)
    {
        try
        {
            foreach (var field in CorePreferenceFields.For(category))
            {
                if ((includeProxy || field.Kind != PreferenceKind.Proxy) && State.Changes.TryGetValue(field.Key, out var value))
                    Parse(field, value);
            }

            if (includeProxy && category == "network" && ProxyValidation.Length > 0)
                return ProxyValidation;
            if (category == "java" && State.HasChanges(category))
            {
                int Number(string key) => int.TryParse(State.Text(CorePreferenceFields.All.First(field => field.Key == key)), out var number) ? number : 0;
                var automatic = State.Text(CorePreferenceFields.All.First(field => field.Key == "memoryMode")) == "auto";
                bool Edited(string first, string second) => State.Changes.ContainsKey(first) || State.Changes.ContainsKey(second) || State.Removed.Contains(first) || State.Removed.Contains(second);
                if (automatic ? Edited("autoMemoryMinMb", "autoMemoryMaxMb") && Number("autoMemoryMinMb") > Number("autoMemoryMaxMb") : Edited("minMemoryMb", "maxMemoryMb") && Number("minMemoryMb") > Number("maxMemoryMb"))
                {
                    return Resources.SettingsMemoryOrder;
                }
            }

            return string.Empty;
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }
    }

    private static JsonNode? Parse(PreferenceField field, string value)
    {
        if (value.Length == 0)
            return null;
        if (field.Key is "width" or "height" || field.Kind == PreferenceKind.Integer)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < field.Minimum || number > field.Maximum)
                throw new ArgumentException(string.Format(Resources.SettingsNumberRange, field.Title(), field.Minimum, field.Maximum));
            return JsonValue.Create(number);
        }

        if (field.Kind == PreferenceKind.Boolean)
        {
            if (!bool.TryParse(value, out var boolean))
                throw new ArgumentException(string.Format(Resources.SettingsInvalidValue, field.Title()));
            return JsonValue.Create(boolean);
        }

        if (field.Kind == PreferenceKind.DownloadSource && value is not ("official" or "bmclapi"))
            throw new ArgumentException(string.Format(Resources.SettingsInvalidValue, field.Title()));

        if (field.Kind == PreferenceKind.Arguments)
            return new JsonArray([.. value.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => (JsonNode?)JsonValue.Create(line.TrimEnd('\r')))]);
        if (field.Kind is PreferenceKind.Environment or PreferenceKind.Mirrors)
        {
            var pairs = new JsonObject();
            foreach (var line in value.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                var separator = line.IndexOf('=');
                if (separator < 1 || line.Contains('\0'))
                    throw new ArgumentException(string.Format(Resources.SettingsInvalidValue, field.Title()));
                var key = line[..separator].Trim();
                var target = line[(separator + 1)..].TrimEnd('\r');
                if (pairs.ContainsKey(key) || key.Length == 0 || (field.Kind == PreferenceKind.Mirrors && (!Http(key) || !key.EndsWith('/') || !Http(target))))
                    throw new ArgumentException(string.Format(Resources.SettingsInvalidValue, field.Title()));
                pairs[key] = target;
            }

            return pairs;
        }

        if (field.Kind == PreferenceKind.Proxy && value != "direct" && (!Uri.TryCreate(value, UriKind.Absolute, out var proxy) || proxy.Scheme is not ("http" or "https" or "socks4" or "socks4a" or "socks5") || string.IsNullOrEmpty(proxy.Host)))
            throw new ArgumentException(Resources.SettingsProxyRequired);
        if (field.Key is "stdoutEncoding" or "stderrEncoding")
        {
            _ = Rodel.MinecraftLauncher.Core.Launch.LogEncoding.Resolve(value, out var warning);
            if (warning is not null)
                throw new ArgumentException(string.Format(Resources.SettingsInvalidValue, field.Title()));
        }

        return JsonValue.Create(value.Trim());
    }

    private static bool Http(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    internal async Task<bool> SaveAsync(string category, bool validOnly = false)
    {
        if (State.Busy || !State.HasChanges(category) || CorePreferenceFields.For(category).Any(field => State.Editing.Contains(field.Key)))
            return false;
        var validation = Validation(category);
        if (!validOnly && validation.Length > 0)
        {
            Publish(State with { Error = validation });
            return false;
        }

        var fields = CorePreferenceFields.For(category);
        if (validOnly)
        {
            fields = [..fields.Where(field =>
                    {
                        if (State.Editing.Contains(field.Key))
                            return false;
                        if (field.Kind == PreferenceKind.Proxy && State.ProxyManual && ProxyValidation.Length > 0)
                            return false;
                        if (!State.Changes.TryGetValue(field.Key, out var text))
                            return true;
                        try
                        {
                            Parse(field, text);
                            return true;
                        }
                        catch (ArgumentException)
                        {
                            return false;
                        }
                    })];
            if (category == "java")
            {
                var automatic = State.Text(CorePreferenceFields.All.First(field => field.Key == "memoryMode")) == "auto";
                var bounds = automatic ? new[]
                {
                    "autoMemoryMinMb",
                    "autoMemoryMaxMb"
                }
                    : ["minMemoryMb", "maxMemoryMb"];
                var values = bounds.Select(key => State.Text(CorePreferenceFields.All.First(field => field.Key == key))).ToArray();
                if (int.TryParse(values[0], out var minimum) && int.TryParse(values[1], out var maximum) && minimum > maximum)
                    fields = [.. fields.Where(field => !bounds.Contains(field.Key))];
            }

            if (!fields.Any(field => State.Changes.ContainsKey(field.Key) || State.Removed.Contains(field.Key)))
                return false;
        }

        var snapshotChanges = State.Changes;
        var snapshotRemoved = State.Removed;
        var changes = new JsonObject();
        var removed = new List<string>();
        foreach (var field in fields)
        {
            var key = category == "network" ? field.Key[8..] : field.Key;
            if (State.Removed.Contains(field.Key))
            {
                removed.Add(key);
            }
            else if (State.Changes.TryGetValue(field.Key, out var value))
            {
                var parsed = Parse(field, value);
                if (parsed is null)
                    removed.Add(key);
                else
                    changes[key] = parsed;
            }
        }

        if (removed.Contains("javaPath"))
        {
            removed.Add("javaRuntimeId");
            removed.Add("javaRuntimeExecutable");
        }

        Publish(State with
        {
            Busy = true,
            Error = string.Empty,
            SavedCategory = string.Empty
        });
        using var ownedWork = _trackWork();
        try
        {
            await Task.Run(() => _write(category, changes, removed, _lifetime));
            var stored = await Task.Run(() => _client.Settings.GetOverrides(), _lifetime);
            var keys = fields.Select(field => field.Key).ToHashSet();
            Publish(State with
            {
                Stored = stored,
                Changes = State.Changes.Where(pair => !keys.Contains(pair.Key) || !snapshotChanges.TryGetValue(pair.Key, out var value) || pair.Value != value).ToDictionary(),
                Removed = State.Removed.Where(key => !keys.Contains(key) || !snapshotRemoved.Contains(key)).ToHashSet(),
                SavedCategory = category,
                Busy = false
            });
            if (!_lifetime.IsCancellationRequested)
                _saved();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Publish(State with
            {
                Busy = false,
                Error = exception.Message
            });
        }

        return State.Error.Length == 0 && !_lifetime.IsCancellationRequested;
    }

    /// <summary>Cancels UI persistence work and waits until its serialized save resource has been released; callers flush before closing.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return new(StartDisposal());
    }

    partial void OnDispose() => _ = StartDisposal();

    private Task StartDisposal() => _disposal ??= DisposeSaveGateAsync();

    private async Task DisposeSaveGateAsync()
    {
        _pendingSave?.Cancel();
        _pendingSave?.Dispose();
        _pendingSave = null;
        await _saveGate.WaitAsync();
        _saveGate.Dispose();
    }
}