using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Tests.Fixtures;
using System.Numerics;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects original-format model interpretation and shared model/pin projection, independently of native generation and GPU presentation.</summary>
[TestClass]
public sealed class SeedSceneGeometryTests
{
    /// <summary>
    /// Review: the same state can have different original biome tints at two voxels. Authored faces
    /// independently select tint indices 0/1 or no tint; a global green texture, palette-only coloring
    /// or an ignored tint index cannot satisfy these distinct multipliers. Raw gray/yellow texels and
    /// cutout alpha must survive for the shader, and an original upward shade override must be kept.
    /// This protects resource/mesh transport, not the native provider's climate/color calculation.
    /// </summary>
    [TestMethod]
    public void OriginalFaceTints_PreservePerVoxelIndicesUntintedFacesAndTextureAlpha()
    {
        var scene = SceneFixture.Tinted();
        var mesh = scene.CreateMesh(64, 64, TestContext.CancellationToken);
        Assert.AreEqual(mesh.Vertices.Length / 6, mesh.VertexColors.Length);
        var expectedFaces = new uint[] { 0x228844, 0xffffff, 0x44aacc, 0xccaa44, 0xffffff, 0xffffff };
        CollectionAssert.AreEqual(expectedFaces.SelectMany(color => Enumerable.Repeat(color, 6)).ToArray(), mesh.VertexColors.ToArray());
        var vertices = mesh.Vertices.Span;
        Assert.AreEqual(.65f, vertices[(6 * 6) + 5], "An untinted downward face retains its independent directional shade.");
        Assert.AreEqual(1f, vertices[(12 * 6) + 5], "The north-facing plant layer uses its original upward shade override.");
        var atlas = scene.TexturePixels.Span;
        var first = ((2 * scene.TextureSize) + 2) * 4;
        CollectionAssert.AreEqual(new byte[] { 128, 128, 128, 255, 222, 194, 95, 255 }, atlas.Slice(first, 8).ToArray(), "Tinting must not replace or desaturate original texels.");
        Assert.AreEqual((byte)0, atlas[(((3 * scene.TextureSize) + 2) * 4) + 3], "Cutout alpha belongs to the original texture.");
        Assert.IsTrue(SceneFixture.Tinted(includeTintData: false).CreateMesh(64, 64, TestContext.CancellationToken).VertexColors.ToArray().All(color => color == 0xffffff), "Missing native tint metadata must not invent a universal foliage color.");
    }

    /// <summary>
    /// Review: a cube-only renderer can look plausible while losing slabs, parent textures or cut boundaries.
    /// Small authored resources establish independent geometric extents and the ten exposed faces of two cubes;
    /// map-PNG and native generation tests do not exercise model JSON. Distant origins must stay local and precise.
    /// </summary>
    [TestMethod]
    public void OriginalModels_RetainHalfHeightInheritanceAndExposedBoundaries()
    {
        var cubes = SceneFixture.Create().CreateMesh(52, 52, TestContext.CancellationToken);
        Assert.AreEqual(10 * 6 * 6, cubes.Vertices.Length, "Two opaque adjacent cubes have ten exposed quads.");
        Assert.AreEqual(Vector3.Zero, cubes.Minimum);
        Assert.AreEqual(new Vector3(2, 1, 1), cubes.Maximum);
        // Native glass resources use an object-valued sprite reference; metadata must not discard otherwise identical geometry.
        var sprite = SceneFixture.Create(spriteObject: true);
        Assert.IsEmpty(sprite.UnavailableModels);
        Assert.AreEqual(cubes.Vertices.Length, sprite.CreateMesh(52, 52, TestContext.CancellationToken).Vertices.Length);
        var scene = SceneFixture.Create(half: true);
        var mesh = scene.CreateMesh(52, 52, TestContext.CancellationToken);
        Assert.IsEmpty(scene.UnavailableModels);
        var halfTop = float.MinValue;
        var vertices = mesh.Vertices.Span;
        for (var i = 0; i < vertices.Length; i += 6)
        {
            Assert.IsTrue(vertices[i + 3] >= 0 && vertices[i + 3] <= 1 && vertices[i + 4] >= 0 && vertices[i + 4] <= 1);
            if (vertices[i] > 1.01f) halfTop = Math.Max(halfTop, vertices[i + 1]);
        }

        Assert.AreEqual(.5f, halfTop);
        var missing = SceneFixture.Create(missing: true);
        CollectionAssert.AreEqual(expected, missing.UnavailableModels.ToArray());
        Assert.AreEqual(6 * 6 * 6, missing.CreateMesh(52, 52, TestContext.CancellationToken).Vertices.Length, "An omitted unsupported block must not hide its neighbor or silently become a cube.");
    }

    /// <summary>
    /// Review: the user's reversed-drag defect is a visible camera contract, not a test of a chosen sign.
    /// Project a near-side point before/after dragging right; it must move right and the fitted scene center
    /// must remain centered. This detects a reversed interaction even when all matrices remain finite.
    /// A container focus must then center that point, retaining the same mesh, angles and zoom; a lost target
    /// would leave the chest away from the center even though the projection still looks plausible.
    /// </summary>
    [TestMethod]
    public void Camera_RightDragMovesNearSurfaceRightAndPinsUseTheSameProjection()
    {
        var mesh = SceneFixture.Create().CreateMesh(52, 52, TestContext.CancellationToken);
        var camera = new SeedSceneCamera();
        var center = (mesh.Minimum + mesh.Maximum) / 2;
        var before = camera.Project(mesh, 800, 560);
        var after = camera.Drag(20, 0).Project(mesh, 800, 560);
        Assert.IsGreaterThan(before.Point(center + Vector3.UnitZ).X, after.Point(center + Vector3.UnitZ).X);
        Assert.AreEqual(400f, after.Point(center).X, .01f);
        Assert.AreEqual(280f, after.Point(center).Y, .01f);
        var clip = Vector4.Transform(new Vector4(center + Vector3.UnitZ, 1), after.Matrix);
        Assert.AreEqual((clip.X + 1) * 400, after.Point(center + Vector3.UnitZ).X, .001f);
        var target = new Vector3(.5f, .5f, .5f);
        var focused = camera.Drag(20, 0) with { Focus = target };
        var located = focused.Project(mesh, 800, 560).Point(target);
        Assert.AreEqual(400f, located.X, .01f);
        Assert.AreEqual(280f, located.Y, .01f);
    }

    /// <summary>Gets or sets the current runner context and cancellation token.</summary>
    public TestContext TestContext { get; set; }

    private static readonly string[] expected = ["test:missing"];
}