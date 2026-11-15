// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects saved/native provenance, confirmed preview eligibility and revision changes; only original generation is substituted.</summary>
[TestClass]
public sealed class WorldMapPreviewTests
{
    /// <summary>
    /// Review: close saved/native joins previously requested only base terrain, then painted missing material
    /// colors with biome legend colors. Existing merge tests supplied already-colored tiles and missed the
    /// request stage. The real planner must request material sampling only within the original 128-block
    /// surface budget and supported adapters; quick previews, far views and slices retain the base request.
    /// The independent original color below must survive the merge instead of becoming a desert legend color.
    /// </summary>
    [TestMethod]
    [DataRow(true, true, 64, 1, null, true)]
    [DataRow(true, true, 64, 2, null, true)]
    [DataRow(true, true, 64, 4, null, false)]
    [DataRow(true, true, 64, 1, 80, false)]
    [DataRow(false, true, 64, 1, null, false)]
    [DataRow(true, false, 16, 8, null, false)]
    public async Task PreviewMaterialSampling_RespectsNativeCapabilityAndCloseSurfaceBudget(bool supported, bool requested, int side, int step, int? slice, bool decorated)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var (selection, _) = await PrepareAsync(paths);
            using var session = await new WorldMapService(paths).OpenAsync(selection, TestContext.CancellationToken);
            var query = new WorldMapQuery { Selection = selection, MinX = 512, Side = side, Step = step, Y = slice, Decorated = requested };
            var mixed = await WorldMapPreview.ReadAsync(session, query, (request, _) =>
            {
                Assert.AreEqual(decorated, request.Decorated);
                var original = Original(request, biomes: false);
                return Task.FromResult(original with
                {
                    Decorated = request.Decorated,
                    SurfaceHeights = [.. original.Heights],
                    WaterDepths = new int[original.Heights.Length]
                });
            }, TestContext.CancellationToken, supported);
            Assert.AreEqual("mixed", mixed.Source);
            Assert.AreEqual(0x123456u, mixed.SurfaceColors[0]);
            Assert.IsFalse(mixed.Saved[0]);
        });
    }

    /// <summary>
    /// A generator may disagree with player edits and may have no biome API. The real saved parser must win,
    /// while missing pixels render exactly like the separate original tile, including void and height slices.
    /// Existing saved-only/export cases cannot detect merging the wrong palette, black filler or lost provenance.
    /// </summary>
    [TestMethod]
    [DataRow(true, null)]
    [DataRow(false, null)]
    [DataRow(true, 80)]
    [DataRow(false, 80)]
    public async Task MixedMap_PreservesSavedPixelsAndIndependentPreviewRendering(bool biomes, int? slice)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var (selection, directory) = await PrepareAsync(paths);
            var hashes = Hashes(directory);
            using var session = await new WorldMapService(paths).OpenAsync(selection, TestContext.CancellationToken);
            Assert.IsTrue(session.Snapshot.PreviewSupported);
            var query = new WorldMapQuery { Selection = selection, MinX = 79, Side = 2, Step = 1, Y = slice };
            var saved = await session.MapAsync(query, TestContext.CancellationToken);
            CollectionAssert.AreEqual(ExpectedSavedSamples, saved.Known);
            SeedMapTile? original = null;
            var mixed = await WorldMapPreview.ReadAsync(session, query, (request, _) =>
            {
                Assert.AreEqual(session.Snapshot.Seed, request.Seed); Assert.AreEqual(session.Snapshot.Profile, request.Profile);
                Assert.AreEqual(session.Snapshot.Settings, request.World); Assert.AreEqual(slice, request.Y);
                Assert.IsEmpty(request.Structures!, "Raster completion must not trigger a structure scan.");
                original = Original(request, biomes);
                return Task.FromResult(original);
            }, TestContext.CancellationToken);
            Assert.AreEqual("mixed", mixed.Source); Assert.AreEqual(saved.Fingerprint, mixed.Fingerprint);
            Assert.AreEqual("native-fixture", mixed.PreviewFingerprint);
            CollectionAssert.AreEqual(saved.Known, mixed.Saved);
            Assert.IsTrue(mixed.Known.All(value => value)); Assert.IsEmpty(mixed.PreviewStructures);
            CollectionAssert.AreEqual(saved.Structures, mixed.Structures);
            var savedPixels = SeedMapImage.Pixels(saved); var previewPixels = SeedMapImage.Pixels(original!); var mixedPixels = SeedMapImage.Pixels(mixed);
            for (var index = 0; index < 4; index++)
                CollectionAssert.AreEqual((saved.Known[index] ? savedPixels : previewPixels)[(index * 4)..((index + 1) * 4)], mixedPixels[(index * 4)..((index + 1) * 4)]);
            var pureSaved = await session.MapAsync(query with { Side = 1 }, TestContext.CancellationToken);
            var png = SeedMapImage.Png(new SeedMapView(79, 0, 1, 2, 2, [pureSaved, mixed]));
            var metadata = JsonSerializer.Deserialize(Metadata(png), SeedJsonContext.Default.SeedMapView)!;
            CollectionAssert.AreEqual(saved.Known, metadata.Tiles[1].Saved);
            Assert.AreEqual("native-fixture", metadata.Tiles[1].PreviewFingerprint);
            Assert.Throws<InvalidDataException>(() => SeedMapImage.Png(new SeedMapView(79, 0, 1, 2, 2,
                [mixed, mixed with { PreviewFingerprint = "another-original-runtime" }])));
            CollectionAssert.AreEqual(hashes, Hashes(directory), "Completion and export cannot write the player save.");
        });
    }

    /// <summary>Native status proves unfinished terrain; corrupt records and completed empty chunks must not be disguised as new predictions. Stored starts remain saved witnesses even in a proto chunk.</summary>
    [TestMethod]
    public async Task MixedMap_OnlyMissingAndNativePreSurfaceStatusesAreEligible()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var (selection, directory) = await PrepareAsync(paths, stages: true);
            using var session = await new WorldMapService(paths).OpenAsync(selection, TestContext.CancellationToken);
            var query = new WorldMapQuery { Selection = selection, Side = 6, Step = 16 };
            var saved = await session.MapAsync(query, TestContext.CancellationToken);
            CollectionAssert.AreEqual(ExpectedStageKnownSamples, saved.Known[..6]);
            CollectionAssert.AreEqual(ExpectedStagePreviewSamples, saved.PreviewEligible[..6]);
            Assert.Contains(warning => warning.StartsWith("WORLD_CHUNK_READ:", StringComparison.Ordinal), saved.Warnings);
            var mixed = await WorldMapPreview.ReadAsync(session, query, (request, _) => Task.FromResult(Original(request)), TestContext.CancellationToken);
            Assert.IsFalse(mixed.Known[4]); Assert.IsFalse(mixed.PreviewEligible[4]);
            Assert.IsTrue(mixed.Known[1]); Assert.IsTrue(mixed.Known[5]);
            Assert.IsTrue(mixed.Saved[2]); Assert.AreEqual(-1, mixed.Heights[2], "A completed empty chunk is actual saved data.");
            Assert.Contains(position => position.X == 25, mixed.Structures);
            var positions = new[] { Witness(25), Witness(64), Witness(80) };
            var filtered = await session.FilterPreviewStructuresAsync("overworld", positions, TestContext.CancellationToken);
            Assert.HasCount(1, filtered); Assert.AreEqual(80, filtered[0].X);
            Assert.IsFalse(await session.HasChangedAsync(TestContext.CancellationToken));
        });
    }

    /// <summary>A fully saved tile requires no runtime; operational or malformed-preview failures preserve actual pixels and warnings without caching a fabricated completion.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MixedMap_OriginalFailureReturnsSavedDataAndCanRetry(bool wrongContext)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var (selection, _) = await PrepareAsync(paths);
            using var session = await new WorldMapService(paths).OpenAsync(selection, TestContext.CancellationToken);
            var full = new WorldMapQuery { Selection = selection, MinX = 12, Side = 1, Step = 1 };
            _ = await WorldMapPreview.ReadAsync(session, full, (_, _) => throw new AssertFailedException("An existing saved sample cannot start native generation."), TestContext.CancellationToken);
            var missing = full with { MinX = 80 };
            var failed = await WorldMapPreview.ReadAsync(session, missing, (request, _) => wrongContext
                ? Task.FromResult(Original(request) with { Seed = "unrelated-world" }) : throw new IOException("isolated original runtime failed"), TestContext.CancellationToken);
            Assert.AreEqual("saved", failed.Source); Assert.IsFalse(failed.Known[0]);
            Assert.Contains(warning => warning.StartsWith("WORLD_PREVIEW_UNAVAILABLE:", StringComparison.Ordinal), failed.Warnings);
            var retry = await WorldMapPreview.ReadAsync(session, missing, (request, _) => Task.FromResult(Original(request)), TestContext.CancellationToken);
            Assert.IsTrue(retry.Known[0]); Assert.AreEqual("mixed", retry.Source);
        });
    }

    /// <summary>The preview callback is held after the saved read, making the actual in-flight change/cancellation boundary explicit; completion cannot publish a stale saved revision.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MixedMap_PendingPreviewCannotCommitAfterChangeOrCancellation(bool cancel)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var (selection, directory) = await PrepareAsync(paths);
            using var session = await new WorldMapService(paths).OpenAsync(selection, TestContext.CancellationToken);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = WorldMapPreview.ReadAsync(session, new() { Selection = selection, MinX = 80, Side = 1, Step = 1 }, async (request, _) =>
            { entered.SetResult(); await release.Task; return Original(request); }, cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
            if (cancel)
            {
                cancellation.Cancel();
            }
            else { var file = Path.Combine(directory, "region", "r.0.0.mca"); File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddSeconds(5)); }

            release.SetResult();
            if (cancel) await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            else await Assert.ThrowsAsync<IOException>(() => pending);
        });
    }

    private static SeedMapTile Original(SeedMapQuery request, bool biomes = true)
    {
        var count = request.Side * request.Side;
        return new()
        {
            Seed = request.Seed,
            World = request.World with { },
            MinX = request.MinX,
            MinZ = request.MinZ,
            Step = request.Step,
            Side = request.Side,
            Y = request.Y,
            DimensionMinY = 0,
            Fingerprint = "native-fixture",
            Palette = biomes ? ["desert"] : [],
            Biomes = [.. Enumerable.Repeat(biomes ? 0 : -1, count)],
            Heights = [.. Enumerable.Range(0, count).Select(index => request.Y ?? (index == 3 ? -1 : 65))],
            Fluids = new int[count],
            SurfaceColors = biomes ? [] : [.. Enumerable.Repeat(0x123456u, count)],
            Structures = [Witness(80)],
        };
    }

    private static SeedPosition Witness(int x) => new() { Id = "village_plains", Kind = "structure", X = x, Y = 70, Z = 8 };

    private static async Task<(WorldMapSelection Selection, string Directory)> PrepareAsync(LauncherPaths paths, bool stages = false)
    {
        var instance = await new InstanceStore(paths).CreateAsync("Saved preview fixture", "1.12.2");
        var directory = Path.Combine(instance.GameDirectory, "saves", "Synthetic"); var region = Path.Combine(directory, "region"); Directory.CreateDirectory(region);
        var fixtures = Path.Combine(AppContext.BaseDirectory, "world-map-fixtures");
        File.Copy(Path.Combine(fixtures, "legacy-level.dat.fixture"), Path.Combine(directory, "level.dat"));
        File.Copy(Path.Combine(fixtures, stages ? "preview-stages.mca.fixture" : "r.0.0.mca.fixture"), Path.Combine(region, "r.0.0.mca"));
        if (!stages) File.Copy(Path.Combine(fixtures, "c.4.0.mcc.fixture"), Path.Combine(region, "c.4.0.mcc"));
        return (new(instance.Id, "Synthetic"), directory);
    }

    private static string[] Hashes(string directory) => [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(file => Path.GetRelativePath(directory, file) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))];

    private static string Metadata(byte[] png)
    {
        for (var offset = 8; offset + 12 <= png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            if (png.AsSpan(offset + 4, 4).SequenceEqual("tEXt"u8))
            { var text = Encoding.UTF8.GetString(png, offset + 8, length); return text[(text.IndexOf('\0') + 1)..]; }

            offset += length + 12;
        }

        throw new InvalidDataException("Missing map provenance metadata.");
    }

    /// <summary>Gets or sets the cancellation and execution context supplied by MSTest.</summary>
    public TestContext TestContext { get; set; }

    private static readonly bool[] ExpectedSavedSamples = [true, false, true, false];
    private static readonly bool[] ExpectedStageKnownSamples = [true, false, true, true, false, false];
    private static readonly bool[] ExpectedStagePreviewSamples = [false, true, false, false, false, true];
}