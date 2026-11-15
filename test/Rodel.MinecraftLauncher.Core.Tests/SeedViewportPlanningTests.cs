// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Features.SeedExplorer;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects visible world coverage and bounded progressive work independently of Sprout controls.</summary>
[TestClass]
public sealed class SeedViewportPlanningTests
{
    /// <summary>
    /// Review: a bookmark saved in a small window can exceed the world-span budget of a larger window. Its valid
    /// overview scale must remain available, while restoration in larger geometry must clamp before sample planning.
    /// Literal legal scale and world-span expectations distinguish this from the independent ruler/coverage cases.
    /// </summary>
    [TestMethod]
    public void RestoredScale_FitsCurrentWindowWithoutRejectingValidOverviewBookmarks()
    {
        Assert.AreEqual(65_536d, SeedMapPlanning.ClampScale(65_536, 800, 560));
        var restored = SeedMapPlanning.ClampScale(65_536, 2400, 1200);
        Assert.IsGreaterThan(4096d, restored);
        Assert.IsLessThanOrEqualTo(59_000_000.1, restored * 2400);
        Assert.AreEqual(8d, SeedMapPlanning.ClampScale(8, 2400, 1200));
        var edge = SeedMapPlanning.Normalize(new(29_999_999, -30_000_000, 65_536, 2400, 1200));
        Assert.IsLessThanOrEqualTo(30_000_000d, edge.X + (edge.Width * edge.BlocksPerPixel / 2));
        Assert.IsGreaterThanOrEqualTo(-30_000_000d, edge.Z - (edge.Height * edge.BlocksPerPixel / 2));
        var enlarged = SeedMapPlanning.Normalize(edge with { Width = 4800, Height = 2400 });
        Assert.IsLessThanOrEqualTo(30_000_000d, enlarged.X + (enlarged.Width * enlarged.BlocksPerPixel / 2));
        Assert.IsGreaterThanOrEqualTo(-30_000_000d, enlarged.Z - (enlarged.Height * enlarged.BlocksPerPixel / 2));
    }

    /// <summary>
    /// Review: a map scale must represent its labeled block distance at the current zoom and remain readable in a
    /// bounded overlay. Literal 1/2/5 distances catch a misleading fixed-width or incorrectly rounded bar; existing
    /// viewport tests cover world sampling and place anchors, not the player's distance reference.
    /// </summary>
    [TestMethod]
    [DataRow(.25d, 20L)]
    [DataRow(4d, 200L)]
    [DataRow(8d, 500L)]
    [DataRow(65_536d, 5_000_000L)]
    public void Scale_MatchesItsLabeledWorldDistanceWithinABoundedOverlay(double blocksPerPixel, long expectedBlocks)
    {
        var scale = SeedMapPlanning.Scale(blocksPerPixel);
        Assert.AreEqual(expectedBlocks, scale.Blocks);
        Assert.AreEqual((double)expectedBlocks, scale.Pixels * blocksPerPixel, .001d);
        Assert.IsTrue(scale.Pixels is >= 32f and <= 80f);
    }

    /// <summary>
    /// Review: a place icon must mean exactly one confirmed coordinate, including nearby places of the same type,
    /// different types and the world origin. Old screen clustering silently mixed them. Existing raster/coverage
    /// cases cannot detect this semantic loss; literal anchors and membership distinguish it without a UI host.
    /// Popup layering, focus and native hit testing remain packaged UIA checks.
    /// </summary>
    [TestMethod]
    public void PointMarkers_KeepNearbyPlacesSeparateAtTheirActualCoordinates()
    {
        SeedPosition[] positions = [
            new() { Kind = "structure", Id = "mineshaft", X = 100, Z = 100 },
            new() { Kind = "structure", Id = "mineshaft", X = 160, Z = 100 },
            new() { Kind = "structure", Id = "village_plains", X = 100, Z = 160 },
            new() { Kind = "origin", Id = "origin", X = 0, Z = 0 },
        ];
        var view = new SeedMapViewport(0, 0, 1, 800, 480);
        var markers = SeedMapPlanning.Points(positions, view);
        Assert.HasCount(4, markers);
        Assert.IsTrue(markers.All(marker => marker.Positions.Length == 1), "A marker may never contain a hidden place list.");
        CollectionAssert.AreEquivalent(positions, markers.Select(marker => marker.Positions[0]).ToArray());
        // The origin is a permanent reference: painting it last keeps overlapping structure icons from hiding it.
        // Membership and coordinate assertions alone cannot detect that loss of visibility.
        Assert.AreEqual("origin", markers[^1].Positions[0].Kind);
        var mine = markers.Single(marker => marker.Id == "structure.mineshaft.100.100");
        Assert.AreEqual(500f, mine.X);
        Assert.AreEqual(340f, mine.Y);
        CollectionAssert.AreEqual(markers.Select(marker => marker.Id).ToArray(),
            SeedMapPlanning.Points(positions.Reverse(), view).Select(marker => marker.Id).ToArray(), "Refresh enumeration order must not change paint/hit order.");
        // Zoom must enlarge readable artwork within a cap without moving its world anchor or changing hit geometry independently.
        float[] sizes = [.. new[] { .25, .75, 1, 4, 16 }.Select(scale =>
        {
            var marker = SeedMapPlanning.Points([positions[0]], view with { X = 100, Z = 100, BlocksPerPixel = scale }).Single();
            Assert.AreEqual(400f, marker.X); Assert.AreEqual(240f, marker.Y);
            Assert.IsTrue(marker.Size is >= 24 and <= 56);
            return marker.Size;
        })];
        Assert.IsTrue(sizes.Zip(sizes.Skip(1), (closer, farther) => closer > farther).All(increases => increases), "Successively closer zoom levels must produce larger icons before hitting the bounds.");
    }

    /// <summary>
    /// Crowded maps must preserve useful literal places without mixing or moving them. The old unconditional
    /// rendering would retain both overlapping mines; arbitrary truncation could lose the selected or saved place.
    /// This extends the distinct-place contract with priority collisions, deterministic refreshes and overview
    /// filtering. Synthetic world points exercise the real planner, not native hit testing or timing.
    /// </summary>
    [TestMethod]
    public void CrowdedMarkers_PrioritizeFocusedPlacesAtStableAnchorsAndReduceOverviewDetail()
    {
        SeedPosition[] positions = [
            new() { Kind = "origin", Id = "origin" },
            new() { Kind = "structure", Id = "mineshaft", X = 100, Z = 100 },
            new() { Kind = "structure", Id = "selected", X = 110, Z = 100 },
            new() { Kind = "structure", Id = "saved", X = -100, Z = -80 },
            new() { Kind = "structure", Id = "mineshaft", X = -105, Z = -80 },
            new() { Kind = "structure", Id = "target", X = -300, Z = -180 },
            new() { Kind = "structure", Id = "ordinary", X = 300, Z = 180 },
        ];
        static int Priority(SeedPosition position) => position.Id switch { "selected" => 1, "saved" => 2, "target" => 3, _ => 4 };
        var near = new SeedMapViewport(0, 0, 1, 800, 480);
        var detailed = SeedMapPlanning.Points(positions, near, Priority);
        string[] expectedNear = ["origin", "selected", "saved", "target", "ordinary"];
        CollectionAssert.AreEquivalent(expectedNear, detailed.Select(marker => marker.Positions[0].Id).ToArray());
        var selected = detailed.Single(marker => marker.Positions[0].Id == "selected");
        Assert.AreEqual(510f, selected.X);
        Assert.AreEqual(340f, selected.Y);
        Assert.AreEqual(positions[2], selected.Positions.Single());
        CollectionAssert.AreEqual(detailed.Select(marker => marker.Id).ToArray(), SeedMapPlanning.Points(positions.Reverse(), near, Priority).Select(marker => marker.Id).ToArray());
        var overview = SeedMapPlanning.Points(positions.Select(position => position with { X = position.X * 64, Z = position.Z * 64 }), near with { BlocksPerPixel = 64 }, Priority);
        string[] expectedOverview = ["origin", "selected", "saved", "target"];
        CollectionAssert.AreEquivalent(expectedOverview, overview.Select(marker => marker.Positions[0].Id).ToArray());
        Assert.IsTrue(overview.Where(marker => marker.Positions[0].Kind != "origin").All(marker => marker.Size < selected.Size));
        Assert.IsTrue(overview.All(marker => marker.Positions.Length == 1));
    }

    /// <summary>
    /// Review: the original spawn can be only a few blocks from coordinate zero. Both permanent targets must remain
    /// at their real anchors even when their graphics collide. The flag's foot remains at its world coordinate;
    /// ordinary structure collision tests do not protect simultaneous origin/spawn visibility at distant zoom.
    /// </summary>
    [TestMethod]
    [DataRow("spawn")]
    [DataRow("origin")]
    public void PermanentAnchors_CoexistWithoutMovingWorldCoordinates(string focused)
    {
        SeedPosition[] positions = [new() { Kind = "origin", Id = "origin" }, new() { Kind = "spawn", Id = "spawn", X = 8, Y = 64, Z = 8 }];
        var markers = SeedMapPlanning.Points(positions, new(8, 8, 64, 800, 480), position => position.Id == focused ? -1 : 4);
        Assert.HasCount(2, markers);
        var marker = markers.Single(item => item.Positions.Single().Kind == "spawn");
        var origin = markers.Single(item => item.Positions.Single().Kind == "origin");
        Assert.AreEqual(32f, marker.Size);
        Assert.AreEqual(20f, origin.Size);
        Assert.AreEqual(400f, marker.X);
        Assert.AreEqual(240f, marker.Y);
        Assert.AreEqual(399.875f, origin.X);
        Assert.AreEqual(239.875f, origin.Y);
        Assert.IsGreaterThan(0f, marker.OffsetX);
        Assert.IsLessThan(0f, marker.OffsetY);
    }

    /// <summary>
    /// The first preview must be substantially cheaper than the final raster, and final samples must cover the
    /// visible rectangle, including negative coordinates and the world edge. This catches wasteful full-size
    /// previews and floor/clamp holes; export pixel tests do not exercise the viewport request planner.
    /// </summary>
    [TestMethod]
    [DataRow(-173d, -917d, 8d)]
    [DataRow(29_998_000d, -29_998_000d, 1d)]
    public void Raster_CoversTheViewWithOneFinalLevelAndALightweightPreview(double x, double z, double scale)
    {
        var view = new SeedMapViewport(x, z, scale, 800, 480);
        var template = Template() with { Decorated = true };
        var plan = SeedMapPlanning.Raster(template, view);
        var target = SeedMapPlanning.Step(view);
        var fine = plan.Where(query => query.Step == target).ToArray();
        var preview = plan.Where(query => query.Step > target).ToArray();
        Assert.IsLessThan(fine.Sum(query => query.Side * query.Side) / 8, preview.Sum(query => query.Side * query.Side),
            "A preview must not spend most of the final raster's sample budget outside the visible area.");
        Assert.IsTrue(plan.All(query => query.Structures is { Length: 0 }), "Slow assembly must not block raster readiness.");
        Assert.IsTrue(preview.All(query => !query.Decorated), "Quick previews must not generate feature worlds.");
        Assert.IsTrue(fine.All(query => query.Decorated == (query.Step * query.Side <= 128)), "Only bounded local tiles request actual features.");
        Assert.IsTrue(SeedMapPlanning.Markers(template, view, out _).All(query => !query.Decorated), "Structure-only queries must never inherit surface generation.");
        Assert.IsTrue(plan.All(query => query.MinX >= -30_000_000 && query.MinZ >= -30_000_000
            && (long)query.MinX + ((query.Side - 1L) * query.Step) < 30_000_000
            && (long)query.MinZ + ((query.Side - 1L) * query.Step) < 30_000_000));
        VerifyCoverage(fine, view);
    }

    /// <summary>
    /// Zooming must retain valid old coverage until replacement samples arrive. The former LOD filter blanked
    /// known terrain immediately on zoom-out; raster planning alone cannot detect losing the displayed fallback.
    /// </summary>
    [TestMethod]
    public void Retargeting_RetainsKnownRasterUntilReplacementArrives()
    {
        var tile = new SeedMapTile { MinX = -1024, MinZ = -1024, Side = 64, Step = 32 };
        var far = new SeedMapViewport(0, 0, 64, 800, 480);
        Assert.IsTrue(SeedMapPlanning.Visible(tile, far));
        Assert.IsTrue(SeedMapPlanning.Visible(tile, far with { BlocksPerPixel = 1 }));
        Assert.IsFalse(SeedMapPlanning.Visible(tile with { MinX = 100_000 }, far));
    }

    /// <summary>
    /// Local views need complete structure coverage. Larger views need a bounded, connected footprint whose
    /// unknown exterior is explicit; far overview must not spend CPU on automatic structure assembly. A nine-island
    /// sample, unbounded scan or disabled local layer would fail these independent area and work-budget checks.
    /// </summary>
    [TestMethod]
    public void StructureCoverage_IsContinuousAndBoundedBeforeOverviewStopsAutomaticWork()
    {
        var local = new SeedMapViewport(700, -350, 4, 800, 480);
        var detailed = SeedMapPlanning.Markers(Template(), local, out var sampled);
        Assert.IsFalse(sampled);
        VerifyCoverage(detailed, local);
        var middle = local with { BlocksPerPixel = 16 };
        var overview = SeedMapPlanning.Markers(Template(), middle, out sampled);
        Assert.IsTrue(sampled);
        Assert.IsTrue(overview.Length is > 9 and <= 64, "Work must stay bounded while expanding beyond nine disconnected samples.");
        var west = overview.Min(query => query.MinX);
        var north = overview.Min(query => query.MinZ);
        var east = overview.Max(query => query.MinX + query.Step);
        var south = overview.Max(query => query.MinZ + query.Step);
        VerifyCoverage(overview, new((west + east) / 2d, (north + south) / 2d, 1, east - west, south - north));
        Assert.IsLessThanOrEqualTo(8192, east - west);
        Assert.IsLessThanOrEqualTo(8192, south - north);
        Assert.IsTrue(overview.All(query => query.Side == 1 && query.Y is not null && !query.Terrain && query.Structures is null),
            "Structure-only requests must avoid terrain work and retain native structure selection.");
        Assert.IsEmpty(SeedMapPlanning.Markers(Template(), local with { BlocksPerPixel = 512 }, out _), "An overview shows focused places without starting ordinary structure scans.");
    }

    private static void VerifyCoverage(SeedMapQuery[] queries, SeedMapViewport view)
    {
        for (var row = 0; row <= 16; row++)
        {
            for (var column = 0; column <= 32; column++)
            {
                var x = view.X + (((column / 32d) - .5) * view.Width * view.BlocksPerPixel);
                var z = view.Z + (((row / 16d) - .5) * view.Height * view.BlocksPerPixel);
                if (column == 32) x = Math.BitDecrement(x);
                if (row == 16) z = Math.BitDecrement(z);
                Assert.Contains(query => x >= query.MinX && x < (long)query.MinX + (query.Side * query.Step)
                    && z >= query.MinZ && z < (long)query.MinZ + (query.Side * query.Step), queries, $"Uncovered world point: {x}, {z}");
            }
        }
    }

    private static SeedMapQuery Template() => new() { Profile = new(), Seed = "-5495201639033488759" };
}