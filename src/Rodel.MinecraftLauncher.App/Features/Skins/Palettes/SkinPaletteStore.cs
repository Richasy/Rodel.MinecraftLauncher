// Copyright (c) Richasy. All rights reserved.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>A palette identity, user-entered name and ordered, independently editable RGBA colors.</summary>
/// <param name = "Id">Stable selection identity, independent of the display language.</param>
/// <param name = "Name">User name, or a built-in identity localized by the interface.</param>
/// <param name = "Colors">Canonical #RRGGBBAA values in swatch order.</param>
internal sealed record SkinPalette(string Id, string Name, string[] Colors);

/// <summary>One atomically committed application preference; built-in colors are never stored or overwritten.</summary>
/// <param name = "SelectedId">Identity selected when the preference was committed.</param>
/// <param name = "Palettes">User-created palettes, excluding built-in presets.</param>
internal sealed record SkinPalettePreference(string SelectedId, SkinPalette[] Palettes)
{
    /// <summary>Gets the presets explicitly removed by the player, independently of custom palettes.</summary>
    public string[] RemovedPresets { get; init; } = [];
}

[JsonSourceGenerationOptions(RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(SkinPalettePreference))]
internal partial class SkinPaletteJsonContext : JsonSerializerContext;

/// <summary>Commits palette changes before publishing them to the editor, without touching any skin document.</summary>
internal sealed class SkinPaletteStore
{
    private readonly Action<string> _write;
    private SkinPalettePreference _preference;

    internal SkinPaletteStore(string stored, Action<string> write)
    {
        _write = write;
        _preference = stored.Length == 0 ? new(SkinEditorPalettes.Ids[0], []) : JsonSerializer.Deserialize(stored, SkinPaletteJsonContext.Default.SkinPalettePreference) ?? throw new InvalidDataException("The palette preference is empty.");
        var ids = new HashSet<string>(SkinEditorPalettes.Ids, StringComparer.Ordinal);
        foreach (var palette in _preference.Palettes)
        {
            if (palette is null || string.IsNullOrWhiteSpace(palette.Id) || !ids.Add(palette.Id) || string.IsNullOrWhiteSpace(palette.Name) || palette.Name.Length > 40 || palette.Colors is null || palette.Colors.Any(color => !TryNormalizeColor(color, out _)))
            {
                throw new InvalidDataException("The palette preference contains an invalid or duplicate palette.");
            }
        }

        if (_preference.RemovedPresets is null || _preference.RemovedPresets.Any(id => !SkinEditorPalettes.Ids.Contains(id, StringComparer.Ordinal)))
            throw new InvalidDataException("The palette preference contains an invalid preset identity.");
        ids.ExceptWith(_preference.RemovedPresets);
        if (!ids.Contains(_preference.SelectedId) && !(ids.Count == 0 && _preference.SelectedId.Length == 0))
            throw new InvalidDataException("The selected palette does not exist.");
        _preference = _preference with
        {
            Palettes = [.._preference.Palettes.Select(palette => palette with { Colors = [..palette.Colors.Select(color =>
                                {
                                    TryNormalizeColor(color, out var normalized);
                                    return normalized;
                                })], })],
        };
    }

    internal SkinPalette[] Items { get => [.. field.Where(palette => !_preference.RemovedPresets.Contains(palette.Id, StringComparer.Ordinal)), .. _preference.Palettes]; } = [.. SkinEditorPalettes.Ids.Select((id, index) => new SkinPalette(id, id, [.. SkinEditorPalettes.Colors[index].Select(color => color + "FF")]))];
    internal SkinPalette Current => Items.FirstOrDefault(palette => palette.Id == _preference.SelectedId) ?? new(string.Empty, string.Empty, []);
    internal bool HasPalette => _preference.SelectedId.Length > 0;
    internal bool IsBuiltIn => SkinEditorPalettes.Ids.Contains(_preference.SelectedId, StringComparer.Ordinal);

    internal void Select(string id)
    {
        if (id == Current.Id)
            return;
        if (!Items.Any(palette => palette.Id == id))
            throw new ArgumentException("The palette does not exist.", nameof(id));
        Commit(_preference with { SelectedId = id });
    }

    internal void Create(string name, bool copy, string? append = null)
    {
        var colors = copy ? Current.Colors.ToArray() : [];
        if (append is not null)
        {
            if (!TryNormalizeColor(append, out var normalized))
                throw new ArgumentException("Invalid RGBA color.", nameof(append));
            colors = [.. colors, normalized];
        }

        Create(name, colors);
    }

    internal void Create(string name, IReadOnlyList<string> colors)
    {
        ValidateName(name);
        var normalized = new string[colors.Count];
        for (var index = 0; index < colors.Count; index++)
        {
            if (!TryNormalizeColor(colors[index], out normalized[index]))
                throw new ArgumentException("Invalid RGBA color.", nameof(colors));
        }

        var palette = new SkinPalette("palette-" + Guid.NewGuid().ToString("N"), name.Trim(), normalized);
        Commit(_preference with { SelectedId = palette.Id, Palettes = [.. _preference.Palettes, palette] });
    }

    internal void Rename(string name)
    {
        EnsureCustom();
        ValidateName(name, Current.Id);
        Replace(Current with { Name = name.Trim() });
    }

    internal void SetColor(int index, string value)
    {
        EnsureCustom();
        if (!TryNormalizeColor(value, out var normalized))
            throw new ArgumentException("Invalid RGBA color.", nameof(value));
        if (Current.Colors[index] == normalized)
            return;
        var colors = Current.Colors.ToArray();
        colors[index] = normalized;
        Replace(Current with { Colors = colors });
    }

    internal void AddColor(string value)
    {
        EnsureCustom();
        if (!TryNormalizeColor(value, out var normalized))
            throw new ArgumentException("Invalid RGBA color.", nameof(value));
        Replace(Current with { Colors = [.. Current.Colors, normalized] });
    }

    internal void RemoveColor(int index)
    {
        EnsureCustom();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Current.Colors.Length);
        Replace(Current with { Colors = [.. Current.Colors.Where((_, position) => position != index)] });
    }

    internal void Delete()
    {
        if (!HasPalette)
            return;
        var id = Current.Id;
        Commit(_preference with
        {
            SelectedId = Items.FirstOrDefault(palette => palette.Id != id)?.Id ?? string.Empty,
            Palettes = [.. _preference.Palettes.Where(palette => palette.Id != id)],
            RemovedPresets = IsBuiltIn ? [.. _preference.RemovedPresets, id] : _preference.RemovedPresets,
        });
    }

    internal static bool TryNormalizeColor(string? value, out string normalized)
    {
        normalized = string.Empty;
        var text = value?.Trim();
        if (text is null || text.Length is not (7 or 9) || text[0] != '#' || !uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        normalized = text.ToUpperInvariant() + (text.Length == 7 ? "FF" : string.Empty);
        return true;
    }

    private void EnsureCustom()
    {
        if (!HasPalette)
            throw new InvalidOperationException("No palette is selected.");
        if (IsBuiltIn)
            throw new InvalidOperationException("Built-in palettes are read-only.");
    }

    private void ValidateName(string name, string? omittedId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Trim().Length > 40 || _preference.Palettes.Any(palette => palette.Id != omittedId && palette.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Palette names must be unique and contain at most 40 characters.", nameof(name));
    }

    private void Replace(SkinPalette palette) => Commit(_preference with { Palettes = [.. _preference.Palettes.Select(item => item.Id == palette.Id ? palette : item)], });

    private void Commit(SkinPalettePreference next)
    {
        _write(JsonSerializer.Serialize(next, SkinPaletteJsonContext.Default.SkinPalettePreference));
        _preference = next;
    }
}