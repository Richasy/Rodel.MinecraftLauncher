// Copyright (c) Richasy. All rights reserved.

using System.ComponentModel;

namespace Rodel.MinecraftLauncher.App.Settings;

/// <summary>Reads typed preferences, persists enum names, and notifies only after synchronous commits.</summary>
internal abstract class SettingsBase(IDictionary<string, object> values) : INotifyPropertyChanged
{
    /// <summary>Reports a preference after its new value has been committed to the backing store.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    protected T GetSetting<T>(T defaultValue, string key) => values.TryGetValue(key, out var raw) && raw is T typed ? typed : defaultValue;

    protected TEnum GetEnumSetting<TEnum>(TEnum defaultValue, string key)
    where TEnum : struct, Enum => values.TryGetValue(key, out var raw) && raw is string text && Enum.TryParse(text, out TEnum value) && Enum.IsDefined(value) ? value : defaultValue;

    protected void SetEnumSetting<TEnum>(TEnum value, TEnum defaultValue, string key)
    where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "The setting must be a defined enum value.");
        SetSetting(value.ToString(), defaultValue.ToString(), key);
    }

    protected void SetSetting<T>(T value, T defaultValue, string key)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (EqualityComparer<T>.Default.Equals(GetSetting(defaultValue, key), value))
            return;
        values[key] = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
    }
}