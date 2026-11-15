// Copyright (c) Richasy. All rights reserved.

using System.Numerics;

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Protects rectangular cape coordinates and the editable/reference scene boundary.</summary>
[TestClass]
public sealed class CapeSceneTests
{
    // Author review: skin PNG tests normalize legacy 64x32 and force Base opacity. Neither is valid
    // for capes. Independent documented UV coordinates and exact raw pixels detect both errors.
    /// <summary>Cape editing, erase, undo and PNG round trips retain rectangular dimensions and alpha.</summary>
    [TestMethod]
    [DataRow(64)]
    [DataRow(128)]
    public void CapePng_RoundTripsIndependentFacesAndTransparentBackground(int size)
    {
        var document = SkinEditor.Create(SkinModel.Cape, "#00000000", size);
        var scale = size / 64;
        document = SkinEditor.Apply(document, [new() { Tool = SkinTool.Pencil, Surface = new() { Part = SkinPart.Cape, Layer = SkinLayer.Base, Face = SkinFace.Front }, X = 2, Y = 3, Color = "#AA550080" }], 0, TestContext.CancellationToken);
        var expected = ((scale + 3) * size) + scale + 2;
        Assert.AreEqual(0xAA550080u, document.Pixels[expected]);
        Assert.ContainsSingle(pixel => pixel != 0, document.Pixels);
        var decoded = SkinPng.Decode(SkinPng.Encode(document), SkinModel.Cape);
        Assert.AreEqual(size / 2, decoded.Height);
        CollectionAssert.AreEqual(document.Pixels, decoded.Pixels);
        var erased = SkinEditor.Apply(document, [new() { Tool = SkinTool.Eraser, Surface = new() { Part = SkinPart.Cape, Layer = SkinLayer.Base, Face = SkinFace.Front }, X = 2, Y = 3 }], 1, TestContext.CancellationToken);
        Assert.AreEqual(0u, erased.Pixels[expected]);
        CollectionAssert.AreEqual(document.Pixels, SkinEditor.Restore(erased, 2, cancellationToken: TestContext.CancellationToken).Pixels);
        var quads = SkinGeometry.Quads(SkinModel.Cape, size);
        Assert.HasCount(6, quads);
        var outside = quads.Single(quad => quad.Texture.Surface.Face == SkinFace.Front);
        Assert.AreEqual(new Vector3(0, 0, -1), Vector3.Normalize(Vector3.Cross(new(outside.Down), new(outside.Right))));
        Assert.AreEqual((scale, scale, 10 * scale, 16 * scale), (outside.Texture.X, outside.Texture.Y, outside.Texture.Width, outside.Texture.Height));
    }

    // Author review: a combined preview can silently resize an HD reference or expose it to painting.
    // Two different resolutions, exact source rows and rays from opposite sides reject those failures.
    /// <summary>Packing preserves both sources and a read-only companion blocks drawing through its body.</summary>
    [TestMethod]
    public void MixedResolutionScene_PreservesRgbaAndPicksOnlyVisiblePrimary()
    {
        var cape = new SkinRenderSnapshot(SkinEditor.Create(SkinModel.Cape, "#CCAA0080", 128));
        var skin = new SkinRenderSnapshot(SkinEditor.Create(SkinModel.Slim, "#117733", 64));
        var before = cape.Pixels.ToArray();
        var scene = new SkinSceneSnapshot(cape, skin);
        Assert.AreEqual(192, scene.Width);
        for (var y = 0; y < cape.Height; y++) CollectionAssert.AreEqual(cape.Pixels.Slice(y * 128, 128).ToArray(), scene.Pixels.Slice(y * 192, 128).ToArray());
        for (var y = 0; y < skin.Height; y++) CollectionAssert.AreEqual(skin.Pixels.Slice(y * 64, 64).ToArray(), scene.Pixels.Slice((y * 192) + 128, 64).ToArray());
        var front = new SkinPickRequest(new(0, 18, 20), new(0, 0, -1));
        Assert.IsNotNull(cape.Pick(front));
        Assert.IsNull(SkinSceneSnapshot.Pick(cape, skin, front));
        var rear = front with { Origin = new(0, 18, -20), Direction = new(0, 0, 1) };
        Assert.AreEqual(SkinFace.Front, SkinSceneSnapshot.Pick(cape, skin, rear)?.Surface.Face);
        var quad = scene.Quads()[0]; quad.Origin[0] = 999;
        Assert.AreNotEqual(999f, scene.Quads()[0].Origin[0]);
        CollectionAssert.AreEqual(before, cape.Pixels.ToArray());
        Assert.Throws<ArgumentException>(() => new SkinSceneSnapshot(cape, cape));
    }

    public TestContext TestContext { get; set; }
}