// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Settings;
using Sprout.Graphics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

/// <summary>Describes one selectable launcher wallpaper and its gallery category.</summary>
/// <param name = "Id">Stable preference and automation identity.</param>
/// <param name = "Path">Absolute path to the packaged or imported image.</param>
/// <param name = "Name">Display name in the current interface language.</param>
/// <param name = "Top">Whether cover cropping keeps the image's upper edge.</param>
/// <param name = "Custom">Whether the image belongs to the user's imported slot.</param>
/// <param name = "Landscape">Whether this is a landscape rather than a player-themed image.</param>
internal sealed record WallpaperItem(
    string Id,
    string Path,
    string Name,
    bool Top = false,
    bool Custom = false,
    bool Landscape = false)
{
    internal ImageSource Source { get; } = ImageSource.FromFile(Path, decodePixelWidth: 2560, decodePixelHeight: 2560);
    internal ImageSource Thumbnail { get; } = ImageSource.FromFile(Path, decodePixelWidth: 480, decodePixelHeight: 480);
    internal ImageAlignment Alignment => new(.5f, Top ? 0f : .5f);
}

internal sealed record WallpaperPreference(string Selected, string? CustomFile = null, string? CustomName = null);

[JsonSerializable(typeof(WallpaperPreference))]
internal partial class WallpaperJsonContext : JsonSerializerContext;

/// <summary>Each theme is committed as one setting; imported originals are copied, never modified.</summary>
internal sealed class WallpaperStore(AppSettings settings, string directory)
{
    private readonly WallpaperPreference[] _preferences = [Read(settings.LightWallpaper, true), Read(settings.DarkWallpaper, false)];
    private readonly bool[] _missingCustom = new bool[2];
    private int _customReadGeneration;

    internal static string DefaultId(bool light) => light ? "light-desert-temple" : "dark-river-valley";

    private static int Index(bool light) => light ? 0 : 1;

    internal static IReadOnlyList<WallpaperItem> BuiltIns(bool light) => light ? [BuiltIn("light-desert-temple", Resources.WallpaperDesert, landscape: true), BuiltIn("light-cherry-meadow", Resources.WallpaperCherry, landscape: true), BuiltIn("light-dappled-autumn", Resources.WallpaperAutumn, landscape: true), BuiltIn("light-snow-peaks", Resources.WallpaperSnow, landscape: true), BuiltIn("light-tropical-bay", Resources.WallpaperTropical, landscape: true), BuiltIn("light-redrock-savanna", Resources.WallpaperSavanna, landscape: true), BuiltIn("light-technical-world-eater", Resources.WallpaperTechnicalLight), BuiltIn("light-parkour-ruins", Resources.WallpaperParkourLight), BuiltIn("light-farming-harvest", Resources.WallpaperFarmingLight), BuiltIn("light-pvp-bedwars", Resources.WallpaperPvpLight), BuiltIn("light-industry-aeronautics", Resources.WallpaperIndustryLight), BuiltIn("light-adventure-discovery", Resources.WallpaperAdventureLight),] : [BuiltIn("dark-river-valley", Resources.WallpaperRiver, landscape: true), BuiltIn("dark-lava-canyon", Resources.WallpaperLava, landscape: true), BuiltIn("dark-luminous-cavern", Resources.WallpaperCavern, landscape: true), BuiltIn("dark-mangrove-water", Resources.WallpaperMangrove, landscape: true), BuiltIn("dark-warped-valley", Resources.WallpaperWarped, landscape: true), BuiltIn("dark-end-islands", Resources.WallpaperEnd, landscape: true, top: true), BuiltIn("dark-technical-iron-farm", Resources.WallpaperTechnicalDark), BuiltIn("dark-parkour-brittle-ice", Resources.WallpaperParkourDark), BuiltIn("dark-farming-night", Resources.WallpaperFarmingDark), BuiltIn("dark-pvp-duel", Resources.WallpaperPvpDark), BuiltIn("dark-industry-create", Resources.WallpaperIndustryDark), BuiltIn("dark-adventure-cavern", Resources.WallpaperAdventureDark),];

    private static WallpaperItem BuiltIn(
        string id,
        string name,
        bool landscape = false,
        bool top = false) => new(
        id,
        System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Backgrounds", (id == "dark-river-valley" ? "river-valley" : id) + ".jpg"),
        name,
        Top: top,
        Landscape: landscape);

    internal WallpaperItem? Custom(bool light)
    {
        var preference = _preferences[Index(light)];
        if (preference.CustomFile is not { Length: > 0 } file || System.IO.Path.GetFileName(file) != file)
            return null;
        var path = System.IO.Path.Combine(directory, file);
        return _missingCustom[Index(light)] ? null : new("custom-" + file, path, preference.CustomName ?? Resources.WallpaperCustom, Custom: true);
    }

    internal async Task<bool> RefreshCustomFilesAsync()
    {
        var generation = ++_customReadGeneration;
        var files = _preferences.Select(preference => preference.CustomFile).ToArray();
        var missing = await Task.Run(() => files.Select(file => file is { Length: > 0 } && System.IO.Path.GetFileName(file) == file && !File.Exists(System.IO.Path.Combine(directory, file))).ToArray());
        if (generation != _customReadGeneration)
            return false;
        var changed = false;
        for (var index = 0; index < files.Length; index++)
        {
            if (_preferences[index].CustomFile != files[index])
                continue;
            changed |= _missingCustom[index] != missing[index];
            _missingCustom[index] = missing[index];
        }

        return changed;
    }

    internal WallpaperItem Current(bool light) => Items(light).FirstOrDefault(item => item.Id == _preferences[Index(light)].Selected) ?? BuiltIns(light)[0];

    internal IReadOnlyList<WallpaperItem> Items(bool light) => Custom(light) is { } custom ? [.. BuiltIns(light), custom] : BuiltIns(light);

    internal void Commit(bool light, WallpaperItem item)
    {
        var previous = _preferences[Index(light)];
        var next = item.Custom ? new WallpaperPreference(item.Id, System.IO.Path.GetFileName(item.Path), item.Name) : previous with
        {
            Selected = item.Id
        };
        Save(light, next);
        if (item.Custom && previous.CustomFile is { } old && old != next.CustomFile && System.IO.Path.GetFileName(old) == old)
            TryDelete(System.IO.Path.Combine(directory, old));
    }

    private void Save(bool light, WallpaperPreference next)
    {
        if (_preferences[Index(light)].CustomFile != next.CustomFile)
            _missingCustom[Index(light)] = false;
        var value = JsonSerializer.Serialize(next, WallpaperJsonContext.Default.WallpaperPreference);
        if (light)
            settings.LightWallpaper = value;
        else
            settings.DarkWallpaper = value;
        _preferences[Index(light)] = next;
    }

    internal void Rename(bool light, string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 80)
            throw new ArgumentException("Wallpaper names must contain between 1 and 80 characters.", nameof(name));
        if (Custom(light) is null)
            throw new InvalidOperationException();
        Save(light, _preferences[Index(light)] with { CustomName = name });
    }

    internal void DeleteCustom(bool light)
    {
        if (Custom(light) is not { } item)
            return;
        var current = _preferences[Index(light)];
        Save(light, new(current.Selected == item.Id ? DefaultId(light) : current.Selected));
        TryDelete(item.Path);
    }

    internal async Task<WallpaperItem> ImportAsync(string source, ImageLoadServices services, CancellationToken cancellationToken)
    {
        var extension = System.IO.Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp"))
            throw new InvalidDataException();
        var destination = System.IO.Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);
        var temporary = destination + ".tmp";
        try
        {
            await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(directory);
                    File.Copy(source, temporary);
                }, cancellationToken);
            var sample = await ImageColorAnalysis.DecodeSampleAsync(ImageSource.FromFile(temporary), services, new(256, 256), cancellationToken);
            _ = sample.Width;
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => File.Move(temporary, destination), cancellationToken);
            return new("custom-" + System.IO.Path.GetFileName(destination), destination, System.IO.Path.GetFileName(source), Custom: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static WallpaperPreference Read(string value, bool light)
    {
        try
        {
            return JsonSerializer.Deserialize(value, WallpaperJsonContext.Default.WallpaperPreference) ?? new(DefaultId(light));
        }
        catch (JsonException)
        {
            return new(DefaultId(light));
        }
    }
}