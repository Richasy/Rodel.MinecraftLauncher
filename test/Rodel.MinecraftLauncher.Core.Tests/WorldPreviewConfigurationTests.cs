// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects the semantic boundary between recorded generator settings and the smaller original preview request.</summary>
[TestClass]
public sealed class WorldPreviewConfigurationTests
{
    /// <summary>
    /// Silently treating an unknown generator as normal produces convincing but unrelated unexplored terrain.
    /// Literal old/new NBT shapes supply the oracle: expressible native presets survive, while custom settings,
    /// flat options, missing generator definitions and experimental features retain saved-only operation.
    /// This distinct gate cannot be established by a successful pixel merge against an already supplied callback.
    /// </summary>
    [TestMethod]
    [DataRow("default", "normal")]
    [DataRow("largeBiomes", "large_biomes")]
    [DataRow("amplified", "amplified")]
    [DataRow("fixed", "single_biome")]
    [DataRow("customized", null)]
    [DataRow("flat", null)]
    [DataRow("missing_dimensions", null)]
    [DataRow("custom_noise", null)]
    [DataRow("experimental", null)]
    [DataRow("native_dimensions", "normal")]
    [DataRow("custom_dimension_type", null)]
    [DataRow("dimension_noise_seed", null)]
    [DataRow("dimension_biome_seed", null)]
    [DataRow("end_biome_seed", null)]
    [DataRow("layered_amplified", null)]
    public void SavedConfiguration_OnlyRepresentableOriginalSettingsEnablePreview(string shape, string? expected)
    {
        Dictionary<string, object> data = new() { ["RandomSeed"] = 357913L, ["generatorName"] = shape };
        Dictionary<string, object> generation = [];
        if (shape is "fixed" or "custom_noise" or "native_dimensions" or "custom_dimension_type" or "dimension_noise_seed" or "dimension_biome_seed" or "end_biome_seed" or "layered_amplified")
        {
            Dictionary<string, object> source = new() { ["type"] = "minecraft:multi_noise", ["preset"] = "minecraft:overworld" };
            if (shape is "fixed" or "custom_noise") source = new() { ["type"] = "minecraft:fixed", ["biome"] = "minecraft:desert" };
            if (shape == "layered_amplified") source = new() { ["type"] = "minecraft:vanilla_layered", ["large_biomes"] = (sbyte)1 };
            Dictionary<string, object> generator = new() { ["type"] = "minecraft:noise", ["settings"] = shape switch { "custom_noise" => "example:custom", "layered_amplified" => "minecraft:amplified", _ => "minecraft:overworld" }, ["biome_source"] = source };
            Dictionary<string, object> dimensions = new() { ["minecraft:overworld"] = new Dictionary<string, object> { ["type"] = "minecraft:overworld", ["generator"] = generator } };
            if (shape is "native_dimensions" or "custom_dimension_type" or "dimension_noise_seed" or "dimension_biome_seed" or "end_biome_seed")
            {
                Dictionary<string, object> netherBiomes = new() { ["type"] = "minecraft:multi_noise", ["preset"] = "minecraft:nether", ["seed"] = shape == "dimension_biome_seed" ? 9L : 357913L };
                dimensions["minecraft:the_nether"] = new Dictionary<string, object>
                {
                    ["type"] = shape == "custom_dimension_type" ? "example:tall_nether" : "minecraft:the_nether",
                    ["generator"] = new Dictionary<string, object> { ["type"] = "minecraft:noise", ["settings"] = "minecraft:nether", ["seed"] = shape == "dimension_noise_seed" ? 8L : 357913L, ["biome_source"] = netherBiomes },
                };
                dimensions["minecraft:the_end"] = new Dictionary<string, object>
                {
                    ["type"] = "minecraft:the_end",
                    ["generator"] = new Dictionary<string, object> { ["type"] = "minecraft:noise", ["settings"] = "minecraft:end", ["biome_source"] = new Dictionary<string, object> { ["type"] = "minecraft:the_end", ["seed"] = shape == "end_biome_seed" ? 7L : 357913L } },
                };
            }

            generation = new() { ["seed"] = 357913L, ["dimensions"] = dimensions };
        }
        else if (shape == "missing_dimensions")
        {
            generation["seed"] = 357913L;
        }
        else if (shape == "experimental") { data["generatorName"] = "default"; data["enabled_features"] = new List<object> { "minecraft:vanilla", "minecraft:trade_rebalance" }; }

        var (Settings, Reason) = WorldPreviewSettings.Read(data, generation);
        if (expected is null)
        {
            Assert.IsNotNull(Reason);
        }
        else
        {
            Assert.IsNull(Reason); Assert.AreEqual(expected, Settings.Preset);
            if (shape == "fixed") Assert.AreEqual("desert", Settings.Biome);
        }
    }

    /// <summary>Provider pack names alone do not prove terrain changes. Inspect the selected local pack's actual generation definitions, allowing resource/loot-only packs while refusing excluded worldgen inputs.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActivePack_OnlyGenerationDefinitionsDisableVanillaPreview(bool worldgen)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var pack = Path.Combine(paths.Root, "datapacks", "selected");
            var file = Path.Combine(pack, "data", "example", worldgen ? "worldgen/biome" : "loot_tables", "definition.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "{}", TestContext.CancellationToken);
            Dictionary<string, object> data = new()
            {
                ["RandomSeed"] = 357913L,
                ["generatorName"] = "default",
                ["DataPacks"] = new Dictionary<string, object> { ["Enabled"] = new List<object> { "vanilla", "fabric", "file/selected" } },
            };
            var (Settings, Reason) = WorldPreviewSettings.Read(data, [], paths.Root);
            Assert.AreEqual(worldgen, Reason is not null);
        });
    }

    /// <summary>Gets or sets the cancellation and execution context supplied by MSTest.</summary>
    public TestContext TestContext { get; set; }
}