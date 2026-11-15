// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Features.Skins.Rendering;
using System.Numerics;

namespace Rodel.MinecraftLauncher.Rendering.Tests;

/// <summary>Protects screen-plane navigation and its agreement with real skin picking.</summary>
[TestClass]
public sealed class SkinCameraPanTests
{
    // Author review: existing render-cache cases change yaw but do not protect drag direction or
    // camera-local pan at magnification. Projection provides a separate oracle from PlanePoint;
    // a known head texel must still be picked at its new projected position.
    /// <summary>A drag moves the model with the pointer at either camera pose and keeps picking attached.</summary>
    [TestMethod]
    [DataRow(0f, 0f, 60f)]
    [DataRow(.65f, -.35f, 28f)]
    public void PanDrag_TracksScreenAxesAndPreservesProjectedTexel(float yaw, float pitch, float distance)
    {
        var camera = new SkinCamera(1440, 900, yaw, pitch, distance, new(1, -2, 0), new(0, 28, 0));
        var anchor = camera.Center;
        var before = Project(camera, anchor);
        var panned = camera with { Pan = camera.Pan + camera.PanDelta(120, -80) };
        var after = Project(panned, anchor);
        Assert.AreEqual(before.X + 120, after.X, .002f, "Rightward dragging must move the model right, not the camera right.");
        Assert.AreEqual(before.Y - 80, after.Y, .002f, "Upward dragging must move the model up at the current rotation and zoom.");

        var texture = new SkinRenderSnapshot(SkinEditor.Create(SkinModel.Classic, "#234567"));
        var texel = new Vector3(.5f, 28.5f, 4);
        var originalPosition = Project(camera, texel);
        var originalHit = texture.Pick(camera.Ray(originalPosition.X, originalPosition.Y, SkinLayer.Base, [SkinPart.Head]));
        Assert.IsNotNull(originalHit);
        Assert.AreEqual(SkinFace.Front, originalHit.Surface.Face);
        var position = Project(panned, texel);
        var hit = texture.Pick(panned.Ray(position.X, position.Y, SkinLayer.Base, [SkinPart.Head]));
        Assert.IsNotNull(hit);
        Assert.AreEqual(originalHit.Surface, hit.Surface);
        Assert.AreEqual(originalHit.X, hit.X);
        Assert.AreEqual(originalHit.Y, hit.Y);
    }

    private static Vector2 Project(SkinCamera camera, Vector3 point)
    {
        var projected = Vector4.Transform(new Vector4(point, 1), camera.ViewProjection);
        return new(((projected.X / projected.W) + 1) * camera.Width / 2f,
            (1 - (projected.Y / projected.W)) * camera.Height / 2f);
    }
}
