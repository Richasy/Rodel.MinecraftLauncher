// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Keeps less frequent world-generation choices in one window-owned light-dismiss surface.</summary>
internal sealed partial class SeedWorldOptionsPopup(SeedExplorerState state) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private static string[] Presets => ["normal", "large_biomes", "amplified", "single_biome"];
    private static string[] PresetNames => [Resources.SeedNormal, Resources.SeedLargeBiomes, Resources.SeedAmplified, Resources.SeedSingleBiome];
    private string[] Biomes => Current.Catalog?.Biomes.Select(item => item.Id).ToArray() ?? [];
    private void Preset(int index)
    {
        if (index < 0 || index >= Presets.Length || Presets[index] == Current.World.Preset) return;
        _ = state.Controller.Map.ChangeWorldAsync(Current.World with { Preset = Presets[index], Biome = index == 3 ? "plains" : null }, Current.SliceY);
    }
    private Ui Body => Column([
        Text(Resources.SeedPreset).FontSize(12f),
        new SeedChoice(LauncherAppearance.Capture(),Resources.SeedPreset, PresetNames, Array.IndexOf(Presets, Current.World.Preset), Preset,
            "seed.preset", !Current.Opening && Current.World.Dimension == "overworld"),
        Column([
            Text(Resources.SeedBiome).FontSize(12f).Foreground(LauncherTheme.Muted),
            new SeedChoice(LauncherAppearance.Capture(),Resources.SeedBiome, [.. Biomes.Select(id => SeedText.Biome(id, Current))], Array.IndexOf(Biomes, Current.World.Biome),
                index => { if (index >= 0 && index < Biomes.Length) _ = state.Controller.Map.ChangeWorldAsync(Current.World with { Biome = Biomes[index] }, Current.SliceY); },
                "seed.single-biome", !Current.Opening && Biomes.Length > 0),
        ]).Spacing(6f).Visible(Current.World.Preset == "single_biome"),
        Row([
            new SeedCommand(LauncherAppearance.Capture(),Resources.SeedRandom, () => _ = state.Controller.Map.RandomAsync(), "seed.random", !Current.Opening).Weight(1f),
            new SeedCommand(LauncherAppearance.Capture(),Resources.SeedCopySeed, () => state.Controller.Copy(Current.Seed), "seed.copy", Current.Seed.Length > 0).Weight(1f),
        ]).Spacing(8f).Visible(Current.Width < 1000),
    ]).Spacing(8f).Width(304f);
}