// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Shares the exact same artwork, line symbol and color between map pins, conditions, layers and legends.</summary>
internal sealed record SeedAppearance(string Asset, string Shape, Color Color)
{
    private static readonly Dictionary<string, ImageSource> Images = [];
    internal ImageSource Image
    {
        get
        {
            if (!Images.TryGetValue(Asset, out var image)) Images[Asset] = image = ImageSource.FromFile(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "SeedExplorer", Asset + ".png"));
            return image;
        }
    }
    internal static bool Symbols => SymbolsFor(LauncherAppearance.Capture());
    internal static bool SymbolsFor(LauncherAppearance theme) => theme.HighContrast;
    internal static SeedAppearance For(SeedPosition position) => For(LauncherAppearance.Capture(), position);
    internal static SeedAppearance For(LauncherAppearance theme, SeedPosition position) => position.Kind == "structure" ? Structure(theme, position.Id)
        : position.Kind == "container" ? Structure(theme, "buried_treasure") : position.Kind == "origin" ? new("biome-plains", "origin", OriginColorFor(theme))
        : position.Kind == "spawn" ? new("biome-plains", "spawn", theme.ProgressAccent) : Biome(theme, position.Biome ?? position.Id);
    internal static Color OriginColor => OriginColorFor(LauncherAppearance.Capture());
    internal static Color OriginColorFor(LauncherAppearance theme) => theme.HighContrast ? theme.Ink : Color.FromHex("#F4DE9B");

    internal static SeedAppearance Biome(string id) => Biome(LauncherAppearance.Capture(), id);
    internal static SeedAppearance Biome(LauncherAppearance theme, string id)
    {
        var rgb = SeedMapImage.BiomeColor(id);
        var color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        var family = id switch
        {
            "nether_wastes" => "nether-wastes",
            "crimson_forest" => "crimson-forest",
            "warped_forest" => "warped-forest",
            "soul_sand_valley" => "soul-sand-valley",
            "basalt_deltas" => "basalt-deltas",
            "the_end" => "end-center",
            "end_highlands" => "end-highlands",
            "end_midlands" => "end-midlands",
            "end_barrens" or "the_void" => "end-barrens",
            "small_end_islands" => "small-end-islands",
            "lush_caves" => "lush-caves",
            "dripstone_caves" => "dripstone-caves",
            "deep_dark" => "deep-dark",
            "sulfur_caves" => "sulfur-caves",
            "dappled_forest" => "dappled-forest",
            "pale_garden" => "pale-garden",
            "cherry_grove" => "cherry-grove",
            "mushroom_fields" => "mushroom-fields",
            "river" or "frozen_river" => "river",
            _ when id.Contains("ocean", StringComparison.Ordinal) => "water",
            _ when id.Contains("birch", StringComparison.Ordinal) => "birch-forest",
            _ when id.Contains("taiga", StringComparison.Ordinal) => "taiga",
            "grove" or "snowy_plains" => "snowy-grove",
            _ when id.Contains("peaks", StringComparison.Ordinal) || id is "snowy_slopes" or "ice_spikes" => "snowy-peaks",
            _ when id.Contains("desert", StringComparison.Ordinal) || id.Contains("beach", StringComparison.Ordinal) => "desert",
            _ when id.Contains("savanna", StringComparison.Ordinal) => "savanna",
            _ when id.Contains("badlands", StringComparison.Ordinal) => "badlands",
            _ when id.Contains("jungle", StringComparison.Ordinal) => "jungle",
            _ when id.Contains("forest", StringComparison.Ordinal) || id.Contains("swamp", StringComparison.Ordinal) => "forest",
            _ => "plains",
        };
        var shape = family switch
        {
            "water" or "river" => "water",
            "desert" or "badlands" => "desert",
            "snowy-peaks" => "mountain",
            "nether-wastes" or "basalt-deltas" or "soul-sand-valley" => "nether",
            "end-center" or "end-highlands" or "end-midlands" or "end-barrens" or "small-end-islands" => "end",
            "lush-caves" or "dripstone-caves" or "deep-dark" or "sulfur-caves" => "cave",
            "plains" => "plains",
            _ => "tree",
        };
        return new("biome-" + (id == "water" ? "ocean" : id).Replace('_', '-'), shape, theme.HighContrast ? Color.FromHex("#00ffff") : theme.Light ? color.Darken(.25f) : color);
    }

    internal static SeedAppearance Structure(string id) => Structure(LauncherAppearance.Capture(), id);
    internal static SeedAppearance Structure(LauncherAppearance theme, string id)
    {
        var asset = id switch
        {
            "village_desert" => "village-desert",
            "village_savanna" => "village-savanna",
            "village_snowy" => "village-snowy",
            "village_taiga" => "village-taiga",
            _ when id.StartsWith("village", StringComparison.Ordinal) => "village",
            _ when id.StartsWith("abandoned_camp", StringComparison.Ordinal) => "abandoned-camp",
            _ when id.StartsWith("ruined_portal", StringComparison.Ordinal) => "ruined-portal",
            _ when id.StartsWith("ocean_ruin", StringComparison.Ordinal) => "ocean-ruins",
            _ when id.StartsWith("shipwreck", StringComparison.Ordinal) => "shipwreck",
            _ when id.StartsWith("mineshaft", StringComparison.Ordinal) => "mineshaft",
            _ => id.Replace('_', '-'),
        };
        var shape = asset switch
        {
            "village" or "village-desert" or "village-savanna" or "village-snowy" or "village-taiga" => "house",
            "desert-pyramid" or "jungle-pyramid" or "monument" => "pyramid",
            "swamp-hut" or "igloo" => "hut",
            "pillager-outpost" => "tower",
            "mansion" => "mansion",
            "ocean-ruins" or "trail-ruins" => "ruins",
            "shipwreck" => "ship",
            "buried-treasure" => "chest",
            "mineshaft" => "mine",
            "stronghold" or "ruined-portal" => "portal",
            "ancient-city" or "trial-chambers" => "city",
            "fortress" or "bastion-remnant" => "fortress",
            "end-city" => "end-city",
            "abandoned-camp" => "camp",
            "nether-fossil" => "bone",
            _ => "tower",
        };
        var hex = shape switch
        {
            "house" or "hut" or "camp" => theme.Light ? "#33755b" : "#86e3b5",
            "pyramid" or "chest" or "ruins" => theme.Light ? "#ac6427" : "#ffcd74",
            "ship" => theme.Light ? "#386e9a" : "#89c5ee",
            "portal" or "end-city" => theme.Light ? "#7951a2" : "#c9a5f0",
            "city" => theme.Light ? "#336f79" : "#83dae3",
            "fortress" or "bone" => theme.Light ? "#a34f4b" : "#ed9b90",
            _ => theme.Light ? "#806446" : "#d9bb8f",
        };
        var image = id switch { "village" => "village_plains", "abandoned_camp" => "abandoned_camp_forest", _ => id };
        return new("structure-" + image.Replace('_', '-'), shape, Color.FromHex(theme.HighContrast ? "#ffeb00" : hex));
    }
}

internal sealed class SeedSymbol(string shape, Color color, float size) : Control
{
    private readonly string _shape = shape;
    private readonly Color _color = color;
    private readonly float _size = size;
    private static readonly Dictionary<string, PathData> Geometry = new()
    {
        ["origin"] = LauncherIcons.WorldOriginGeometry,
        ["spawn"] = LauncherIcons.WorldSpawnGeometry,
        ["house"] = PathData.Parse("M3 11 L12 4 L21 11 M5 10 V21 H19 V10 M10 21 V14 H14 V21"),
        ["pyramid"] = PathData.Parse("M2 21 H22 M4 21 V17 H7 V13 H10 V8 H14 V13 H17 V17 H20 V21 M11 21 V17 H13 V21"),
        ["hut"] = PathData.Parse("M3 10 L12 3 L21 10 H3 M6 10 V18 H18 V10 M7 18 V22 M17 18 V22 M10 14 H14"),
        ["tower"] = PathData.Parse("M7 22 V9 H17 V22 M5 9 V5 H8 V3 H16 V5 H19 V9 Z M10 13 H14 M10 17 H14"),
        ["mansion"] = PathData.Parse("M2 10 L6 6 H18 L22 10 M3 10 V21 H21 V10 M7 10 V21 M17 10 V21 M10 21 V15 H14 V21 M10 11 H14"),
        ["ruins"] = PathData.Parse("M3 22 H22 M5 22 V10 H9 V22 M15 22 V5 H19 V22 M4 10 H10 M14 5 H20 M9 16 H15"),
        ["ship"] = PathData.Parse("M2 15 H22 L18 21 H7 Z M12 3 V15 M12 3 L20 11 H12 M2 23 Q6 20 10 23 Q14 20 18 23"),
        ["chest"] = PathData.Parse("M3 7 H21 V21 H3 Z M3 12 H21 M10 11 H14 V15 H10 Z M6 7 V4 H18 V7"),
        ["mine"] = PathData.Parse("M3 22 V7 L12 2 L21 7 V22 M6 22 V9 H18 V22 M8 22 L11 13 M16 22 L13 13 M7 18 H17 M9 14 H15"),
        ["portal"] = PathData.Parse("M5 22 V7 L9 3 H15 L19 7 V22 M8 22 V8 H16 V22 M3 22 H21 M10 12 L14 16 M14 11 L10 17"),
        ["city"] = PathData.Parse("M3 22 V10 H7 V22 M17 22 V7 H21 V22 M7 15 H17 M9 22 V10 L12 6 L15 10 V22 M1 22 H23"),
        ["fortress"] = PathData.Parse("M3 22 V8 H7 V12 H17 V5 H21 V22 M3 8 V4 H7 V8 M17 5 V2 H21 V5 M7 17 H17 M9 22 V18 H15 V22"),
        ["end-city"] = PathData.Parse("M10 22 V6 H14 V22 M8 6 V2 H16 V6 Z M10 12 H3 V7 H7 V12 M14 16 H21 V9 H17 V16 M7 22 H17"),
        ["camp"] = PathData.Parse("M2 21 L12 3 L22 21 Z M12 3 V21 M8 21 L12 13 L16 21 M2 7 H6 M18 5 H22"),
        ["bone"] = PathData.Parse("M3 18 Q9 3 18 7 M6 12 L11 19 M9 8 L14 16 M13 6 L18 13 M18 7 L22 4 M18 7 L22 9"),
        ["tree"] = PathData.Parse("M12 2 L5 10 H8 L3 17 H10 V22 H14 V17 H21 L16 10 H19 Z"),
        ["water"] = PathData.Parse("M2 6 Q5 2 8 6 Q11 10 14 6 Q17 2 22 6 M2 13 Q5 9 8 13 Q11 17 14 13 Q17 9 22 13 M2 20 Q5 16 8 20 Q11 24 14 20 Q17 16 22 20"),
        ["mountain"] = PathData.Parse("M2 22 L10 3 L18 22 M13 12 L17 7 L23 22 M7 10 L10 13 L13 10"),
        ["desert"] = PathData.Parse("M11 22 V5 Q11 1 14 5 V22 M11 13 H6 V8 M14 16 H19 V10 M3 22 H22"),
        ["nether"] = PathData.Parse("M3 22 V12 L8 7 L10 13 L15 2 L20 12 V22 M7 22 V16 M15 22 V13"),
        ["end"] = PathData.Parse("M2 13 L10 5 L22 10 L16 20 L4 19 Z M2 13 L13 15 L22 10 M13 15 L16 20"),
        ["cave"] = PathData.Parse("M2 22 V12 L7 5 H17 L22 12 V22 M7 22 V14 L12 10 L17 14 V22 M9 5 L10 10 M16 5 L15 9"),
        ["plains"] = PathData.Parse("M3 22 H22 M6 22 V13 L2 9 M6 16 L10 11 M15 22 V8 M15 13 L20 8 M12 5 H18"),
    };

    /// <inheritdoc/>
    protected override void OnMount()
    {
        SetProjection(new LineIconWidget(Geometry.GetValueOrDefault(_shape, Geometry["origin"]), _size, false, _color), null, null);
    }
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is SeedSymbol other && Render is LineIconWidget widget)
            widget.Configure(Geometry.GetValueOrDefault(other._shape, Geometry["origin"]), other._size, false, other._color);
    }
}

[View]
internal readonly partial record struct SeedVisual(SeedAppearance Appearance, bool Symbols, float Size)
{
    /// <summary>Uses the same category artwork or contextual line symbol at the requested map density.</summary>
    public Ui Body => When(Symbols, new SeedLineVisual(Appearance.Shape, Appearance.Color, Size), new SeedAssetImage(Appearance.Image, Size));
}

[View]
internal readonly partial record struct SeedAssetImage(ImageSource Source, float Size)
{
    /// <summary>Renders a category illustration with its original alpha channel and no opaque background.</summary>
    public Ui Body => Image(Source).Width(Size).Height(Size).Stretch(ImageStretch.Uniform);
}

[View]
internal readonly partial record struct SeedLineVisual(string Shape, Color Color, float Size)
{
    /// <summary>Hosts a reconciled contextual line icon with its shared category color.</summary>
    public Ui Body => new SeedSymbol(Shape, Color, Size);
}