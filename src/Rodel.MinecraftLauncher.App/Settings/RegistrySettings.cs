// Copyright (c) Richasy. All rights reserved.

using Microsoft.Win32;
using System.Collections;

namespace Rodel.MinecraftLauncher.App.Settings;

/// <summary>Commits the app's string and Boolean EXE preferences to the current user's registry without package identity.</summary>
internal sealed class RegistrySettings(string path) : IDictionary<string, object>
{
    /// <inheritdoc/>
    public object this[string key]
    {
        get => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            ArgumentNullException.ThrowIfNull(value);
            if (value is not (string or bool))
                throw new ArgumentException("App preferences must be strings or Boolean values.", nameof(value));
            using var registry = Registry.CurrentUser.CreateSubKey(path);
            registry.SetValue(key, value is bool enabled ? enabled ? 1 : 0 : value,
                value is bool ? RegistryValueKind.DWord : RegistryValueKind.String);
        }
    }

    /// <inheritdoc/>
    public ICollection<string> Keys
    {
        get
        {
            using var registry = Registry.CurrentUser.OpenSubKey(path);
            return registry?.GetValueNames() ?? [];
        }
    }

    /// <inheritdoc/>
    public ICollection<object> Values => [.. Keys.Select(key => this[key])];
    /// <inheritdoc/>
    public int Count => Keys.Count;
    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public bool TryGetValue(string key, out object value)
    {
        using var registry = Registry.CurrentUser.OpenSubKey(path);
        var stored = registry?.GetValue(key, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        value = stored is int enabled ? enabled != 0 : stored!;
        return stored is not null;
    }

    /// <inheritdoc/>
    public bool ContainsKey(string key) => TryGetValue(key, out _);
    /// <inheritdoc/>
    public void Add(string key, object value)
    {
        if (ContainsKey(key))
            throw new ArgumentException("The preference already exists.", nameof(key));
        this[key] = value;
    }

    /// <inheritdoc/>
    public bool Remove(string key)
    {
        using var registry = Registry.CurrentUser.OpenSubKey(path, writable: true);
        if (registry?.GetValue(key) is null)
            return false;
        registry.DeleteValue(key, throwOnMissingValue: false);
        return true;
    }

    /// <inheritdoc/>
    public void Clear()
    {
        foreach (var key in Keys)
            Remove(key);
    }

    /// <inheritdoc/>
    public void Add(KeyValuePair<string, object> item) => Add(item.Key, item.Value);
    /// <inheritdoc/>
    public bool Contains(KeyValuePair<string, object> item) => TryGetValue(item.Key, out var value) && Equals(value, item.Value);
    /// <inheritdoc/>
    public bool Remove(KeyValuePair<string, object> item) => Contains(item) && Remove(item.Key);
    /// <inheritdoc/>
    public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) => Keys.Select(key => new KeyValuePair<string, object>(key, this[key])).ToArray().CopyTo(array, arrayIndex);
    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => Keys.Select(key => new KeyValuePair<string, object>(key, this[key])).GetEnumerator();
    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}