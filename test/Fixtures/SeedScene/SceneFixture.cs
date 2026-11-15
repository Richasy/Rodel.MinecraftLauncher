// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using StbImageWriteSharp;
using System.IO.Compression;
using System.Text;

namespace Rodel.MinecraftLauncher.Tests.Fixtures;

/// <summary>Small synthetic original-format resources; no game binaries, player files or original texture assets are committed.</summary>
internal static class SceneFixture
{
    internal static SeedBlockScene Create(bool half = false, bool missing = false, bool spriteObject = false)
    {
        using var jar = new MemoryStream();
        using (var archive = new ZipArchive(jar, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "assets/test/blockstates/solid.json", """{"variants":{"":{"model":"test:block/solid"}}}""");
            Add(archive, "assets/test/blockstates/half.json", """{"variants":{"type=bottom":{"model":"test:block/half"}}}""");
            Add(archive, "assets/test/models/block/solid.json", spriteObject
                ? """{"parent":"test:block/cube","textures":{"all":{"sprite":"test:block/red","force_translucent":true}}}"""
                : """{"parent":"test:block/cube","textures":{"all":"test:block/red"}}""");
            Add(archive, "assets/test/models/block/half.json", """{"parent":"test:block/cube","textures":{"all":"test:block/red"},"elements":[{"from":[0,0,0],"to":[16,8,16],"faces":{"down":{"texture":"#all","cullface":"down"},"up":{"texture":"#all"},"north":{"texture":"#all","cullface":"north"},"south":{"texture":"#all","cullface":"south"},"west":{"texture":"#all","cullface":"west"},"east":{"texture":"#all","cullface":"east"}}}]}""");
            Add(archive, "assets/test/models/block/cube.json", """{"elements":[{"from":[0,0,0],"to":[16,16,16],"faces":{"down":{"texture":"#all","cullface":"down"},"up":{"texture":"#all","cullface":"up"},"north":{"texture":"#all","cullface":"north"},"south":{"texture":"#all","cullface":"south"},"west":{"texture":"#all","cullface":"west"},"east":{"texture":"#all","cullface":"east"}}}]}""");
            using var texture = archive.CreateEntry("assets/test/textures/block/red.png").Open();
            new ImageWriter().WritePng([255, 0, 0, 255], 1, 1, ColorComponents.RedGreenBlueAlpha, texture);
        }

        jar.Position = 0;
        return SeedModelResources.Load(new()
        {
            Seed = "1",
            Bounds = new() { MinX = -320000, MaxX = -319999, MinY = 52, MaxY = 52, MinZ = -768000, MaxZ = -768000 },
            Palette = [new() { Id = "test:solid" }, new() { Id = missing ? "test:missing" : half ? "test:half" : "test:solid", Properties = new() { ["type"] = "bottom" } }],
            Blocks = [0, 0, 1, 0],
        }, jar);
    }

    internal static SeedBlockScene Tinted(bool includeTintData = true)
    {
        using var jar = new MemoryStream();
        using (var archive = new ZipArchive(jar, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "assets/test/blockstates/tinted.json", """{"variants":{"":{"model":"test:block/tinted"}}}""");
            Add(archive, "assets/test/models/block/tinted.json", """
                {"textures":{"all":"test:block/mottled"},"elements":[
                  {"from":[0,0,0],"to":[16,16,16],"faces":{
                    "up":{"texture":"#all","tintindex":0},"down":{"texture":"#all"}}},
                  {"from":[0,0,0],"to":[16,16,16],"shade_direction_override":"up","faces":{
                    "north":{"texture":"#all","tintindex":1}}}
                ]}
                """);
            using var texture = archive.CreateEntry("assets/test/textures/block/mottled.png").Open();
            new ImageWriter().WritePng([128, 128, 128, 255, 222, 194, 95, 255, 0, 0, 0, 0, 16, 32, 48, 255], 2, 2, ColorComponents.RedGreenBlueAlpha, texture);
        }

        jar.Position = 0;
        return SeedModelResources.Load(new()
        {
            Bounds = new() { MinX = 0, MaxX = 1, MinY = 64, MaxY = 64, MinZ = 0, MaxZ = 0 },
            Palette = [new() { Id = "test:tinted" }],
            Blocks = [0, 0, 0, 0],
            TintPalette = includeTintData ? [new() { BiomeId = "test:wet", Colors = [0x228844, 0x44aacc] }, new() { BiomeId = "test:dry", Colors = [0xccaa44, -1] }] : [],
            TintIndices = includeTintData ? [0, 0, 1, 0] : [],
        }, jar);
    }

    private static void Add(ZipArchive archive, string path, string text)
    {
        using var stream = archive.CreateEntry(path).Open();
        stream.Write(Encoding.UTF8.GetBytes(text));
    }
}