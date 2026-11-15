// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Settings;

/// <summary>Generates a LocalSettings-backed partial property with this default value.</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class SettingPropertyAttribute(object defaultValue) : Attribute
{
    public object DefaultValue { get; } = defaultValue;
}