// Copyright (c) Richasy. All rights reserved.

using StbImageSharp;
using System.Globalization;

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Checks physical picking and final PNG pixels against independent model/template witnesses.</summary>
[TestClass]
public sealed class SkinGeometryConsistencyTests
{
    /// <summary>All part/layer/face orientations must pick the requested texel and export it at the standard atlas position, not merely agree with their own generated geometry.</summary>
    [TestMethod]
    [DataRow(SkinModel.Classic, 64)]
    [DataRow(SkinModel.Classic, 128)]
    [DataRow(SkinModel.Slim, 64)]
    [DataRow(SkinModel.Slim, 128)]
    public void PhysicalRays_PaintAndExportAtIndependentTemplateCoordinates(SkinModel model, int size)
    {
        var document = SkinEditor.Create(model, "#000000", size);
        var witnesses = SkinTemplateWitness.Pixels(model, size).ToArray();
        var edits = new List<SkinEdit>();
        foreach (var witness in witnesses)
        {
            var hit = SkinGeometry.Pick(document, new(witness.Origin, witness.Direction, witness.Surface.Layer,
                [witness.Surface.Part], SkipTransparent: false));
            Assert.IsNotNull(hit, $"Ray missed {witness.Surface}.");
            Assert.AreEqual(witness.Surface, hit.Surface);
            Assert.AreEqual((witness.X, witness.Y), (hit.X, hit.Y));
            edits.Add(new()
            {
                Tool = SkinTool.Stroke,
                Surface = hit.Surface,
                Points = [new(hit.LocalX, hit.LocalY)],
                Color = "#" + witness.Color.ToString("X8", CultureInfo.InvariantCulture)
            });
        }

        document = SkinEditor.Apply(document, edits, 0, TestContext.CancellationToken);
        var image = ImageResult.FromMemory(SkinPng.Encode(document), ColorComponents.RedGreenBlueAlpha);
        Assert.AreEqual((size, size), (image.Width, image.Height));
        foreach (var witness in witnesses)
        {
            var offset = ((witness.AtlasY * size) + witness.AtlasX) * 4;
            var actual = ((uint)image.Data[offset] << 24) | ((uint)image.Data[offset + 1] << 16)
                | ((uint)image.Data[offset + 2] << 8) | image.Data[offset + 3];
            Assert.AreEqual(witness.Color, actual, $"Wrong exported texel for {witness.Surface}, {witness.X}/{witness.Y}.");
        }
    }

    /// <summary>Gets or sets the test context supplying cooperative cancellation.</summary>
    public TestContext TestContext { get; set; }
}