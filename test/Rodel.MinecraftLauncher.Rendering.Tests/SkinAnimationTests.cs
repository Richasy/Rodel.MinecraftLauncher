// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Features.Skins.Rendering;
using Sprout.Graphics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Rodel.MinecraftLauncher.Rendering.Tests;

/// <summary>Protects the walking loop and actual shader path; neighboring cases own worker lifetime failures.</summary>
[TestClass]
public sealed class SkinAnimationTests
{
    // Author review: retain the existing walk contract after removing the other modes. World-space
    // hands/feet and their velocities reject a discontinuous reset; the motion check rejects a frozen pose.
    /// <summary>The walking loop joins in position and velocity while visibly moving during its cycle.</summary>
    [TestMethod]
    public void LoopWrap_PreservesLandmarksAndVelocity()
    {
        const double period = SkinAnimation.Duration;
        const double step = .0001;
        var start = Landmarks(SkinAnimation.Sample(0));
        var end = Landmarks(SkinAnimation.Sample(period));
        var before = Landmarks(SkinAnimation.Sample(period - step));
        var after = Landmarks(SkinAnimation.Sample(step));
        for (var i = 0; i < start.Length; i++)
        {
            Assert.IsLessThan(.0001f, Vector3.Distance(start[i], end[i]), $"Endpoint landmark {i}");
            var arrival = (start[i] - before[i]) / (float)step;
            var departure = (after[i] - start[i]) / (float)step;
            Assert.IsLessThan(.2f, Vector3.Distance(arrival, departure), $"Velocity landmark {i}");
        }

        var moved = Enumerable.Range(1, 12).Any(frame =>
            Landmarks(SkinAnimation.Sample(period * frame / 12)).Where((point, i) => Vector3.Distance(point, start[i]) > 2).Any());
        Assert.IsTrue(moved, "A frozen pose is seamless but is not an animation.");
    }

    // Independent rest-mesh bounds remain the severe-penetration oracle for the retained walk.
    // Proximal shoulders/hips meet intentionally; a half-texel margin excludes clothing contact.
    /// <summary>Both arm models keep distal walking limb interiors outside the other body parts.</summary>
    [TestMethod]
    public void Walking_KeepsDistalLimbsOutsideOtherBodyParts()
    {
        foreach (var model in new[] { SkinModel.Classic, SkinModel.Slim })
        {
            var bounds = Bounds(model);
            for (var seconds = 0.0; seconds < SkinAnimation.Duration; seconds += .01)
            {
                var pose = SkinAnimation.Sample(seconds);
                for (var limb = 2; limb < 6; limb++)
                {
                    for (var other = 0; other < 6; other++)
                    {
                        if (limb == other) continue;
                        Matrix4x4.Invert(pose.Parts[other], out var inverse);
                        var intoOther = pose.Parts[limb] * inverse;
                        for (var y = bounds[limb].Low.Y + .5f; y < (limb < 4 ? 20 : 9); y++)
                        {
                            for (var x = bounds[limb].Low.X + .5f; x < bounds[limb].High.X; x++)
                            {
                                for (var z = bounds[limb].Low.Z + .5f; z < bounds[limb].High.Z; z++)
                                {
                                    var point = Vector3.Transform(new Vector3(x, y, z), intoOther);
                                    Assert.IsFalse(Inside(point, bounds[other], .5f), $"{model}, t={seconds}, {(SkinPart)limb} inside {(SkinPart)other}, local {point}");
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private static bool Inside(Vector3 p, (Vector3 Low, Vector3 High) box, float margin) =>
        p.X > box.Low.X + margin && p.X < box.High.X - margin
        && p.Y > box.Low.Y + margin && p.Y < box.High.Y - margin
        && p.Z > box.Low.Z + margin && p.Z < box.High.Z - margin;

    private static (Vector3 Low, Vector3 High)[] Bounds(SkinModel model) => [.. Enum.GetValues<SkinPart>().Where(part => part != SkinPart.Cape).Select(part =>
    {
        var corners = SkinGeometry.Quads(model).Where(q => q.Texture.Surface.Part == part && q.Texture.Surface.Layer == SkinLayer.Base)
            .SelectMany(q => new[] { new Vector3(q.Origin), new Vector3(q.Origin) + new Vector3(q.Right),
                new Vector3(q.Origin) + new Vector3(q.Down), new Vector3(q.Origin) + new Vector3(q.Right) + new Vector3(q.Down) }).ToArray();
        return (corners.Aggregate(Vector3.Min), corners.Aggregate(Vector3.Max));
    })];

    // Author review: retain the actual D3D boundary, which pure pose checks cannot reach. Two walking
    // instants reject stale matrices; restoring static output detects state leaking back into the editor.
    /// <summary>Real D3D frames apply time-varying walking, restore the static scene, and preserve the source composite.</summary>
    [TestMethod]
    public async Task AnimatedGpuFrames_ApplyWalkingAndRestoreStaticScene()
    {
        var document = SkinEditor.Create(SkinModel.Slim, size: 128);
        Array.Fill(document.Pixels, 0xC07050FFu);
        var texture = new SkinRenderSnapshot(document);
        var original = texture.Pixels.ToArray();
        using var presenter = new SkinPreviewEffect(texture);
        var ready = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new SkinGpuRenderer(_ => { }, _ => { }, error => ready.TrySetException(new InvalidOperationException(error)),
            (_, bytes, _, _) => ready.TrySetResult(bytes));
        async Task<byte[]> Draw(SkinAnimationPose? pose, bool antialias = false)
        {
            ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var frame = new SkinGpuFrame(texture, new(256, 256, -.4f, -.14f, 76), 63, true, false, new(.12f, .12f, .12f, 1), Pose: pose, Antialias: antialias);
            renderer.Submit(frame);
            var pixels = await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationToken);
            if (pose is not null)
            {
                // Author review: the new popup background shortcut must not clip visible moving limbs.
                // Use the independent D3D output as the coverage oracle rather than re-projecting its corners here.
                var bounds = MemoryMarshal.Read<Vector4>(presenter.Create(frame).ConstantBuffer.Span[(1496 * sizeof(float))..]);
                var foreground = 0; var clipped = 0;
                for (var y = 0; y < 256; y++)
                {
                    for (var x = 0; x < 256; x++)
                    {
                        var index = ((y * 256) + x) * 4;
                        if (pixels[index] <= 32 && pixels[index + 1] <= 32 && pixels[index + 2] <= 32) continue;
                        foreground++;
                        if (x + .5f < bounds.X || y + .5f < bounds.Y || x + .5f > bounds.Z || y + .5f > bounds.W) clipped++;
                    }
                }

                Assert.IsGreaterThan(100, foreground, "The coverage oracle must contain a visible figure.");
                Assert.AreEqual(0, clipped, "The popup's background shortcut must preserve every visible limb pixel.");
                Assert.IsLessThan(256f * 256f, (bounds.Z - bounds.X) * (bounds.W - bounds.Y), "The normal camera must skip empty background work.");
            }

            return pixels;
        }

        try
        {
            var baseline = await Draw(null);
            var walk = await Draw(SkinAnimation.Sample(0), antialias: true);
            Assert.IsFalse(baseline.AsSpan().SequenceEqual(walk), "The shader must apply joint transforms.");
            var later = await Draw(SkinAnimation.Sample(SkinAnimation.Duration / 2), antialias: true);
            Assert.IsFalse(walk.AsSpan().SequenceEqual(later), "Walking must advance with elapsed time.");
            CollectionAssert.AreEqual(baseline, await Draw(null), "Leaving preview must clear joint state.");
            CollectionAssert.AreEqual(original, texture.Pixels.ToArray(), "Preview cannot paint into the composite.");
        }
        finally
        {
            renderer.Stop();
            await renderer.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
        }
    }

    // Author review: the reported failure is aliased outer geometry, which motion/reset tests cannot detect.
    // A solid white cuboid on black has no low-valued material texels: fractional silhouette coverage is an
    // independent oracle. The unfiltered path rejects a vacuous always-blended fixture; an interior pixel
    // must remain exact, distinguishing coverage AA from simply blurring the whole preview image.
    /// <summary>Actual D3D preview sampling blends the silhouette while preserving fully covered skin pixels.</summary>
    [TestMethod]
    public async Task PreviewAntialiasing_BlendsSilhouetteAndPreservesInterior()
    {
        var document = SkinEditor.Create(SkinModel.Classic);
        Array.Fill(document.Pixels, 0xFFFFFFFFu);
        var texture = new SkinRenderSnapshot(document);
        var ready = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new SkinGpuRenderer(_ => { }, _ => { }, error => ready.TrySetException(new InvalidOperationException(error)),
            (_, bytes, _, _) => ready.TrySetResult(bytes));
        async Task<byte[]> Draw(bool antialias)
        {
            ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            renderer.Submit(new(texture, new(256, 256, -.45f, -.22f, 55, Pivot: new(0, 18, 0)),
                1 << (int)SkinPart.Body, false, false, new(0, 0, 0, 1), Antialias: antialias));
            return await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationToken);
        }

        try
        {
            var point = await Draw(false);
            var smooth = await Draw(true);
            var pointCoverage = 0; var smoothCoverage = 0;
            for (var pixel = 0; pixel < point.Length; pixel += 4)
            {
                if (point[pixel] is > 0 and < 100) pointCoverage++;
                if (smooth[pixel] is > 0 and < 100) smoothCoverage++;
            }

            Assert.AreEqual(0, pointCoverage, "The solid fixture must contain no low-valued material colors.");
            Assert.IsGreaterThan(20, smoothCoverage, "Silhouette pixels need fractional coverage rather than all-or-nothing hits.");
            var center = ((128 * 256) + 128) * 4;
            Assert.IsGreaterThan(150, point[center], "The interior checkpoint must hit the cuboid.");
            CollectionAssert.AreEqual(point[center..(center + 4)], smooth[center..(center + 4)], "AA must preserve an opaque interior texel.");
        }
        finally
        {
            renderer.Stop();
            await renderer.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
        }
    }

    private static Vector3[] Landmarks(SkinAnimationPose pose) => [
        Vector3.Transform(new(0, 32, 4), pose.Parts[(int)SkinPart.Head]),
        Vector3.Transform(new(-6, 12, 0), pose.Parts[(int)SkinPart.RightArm]),
        Vector3.Transform(new(6, 12, 0), pose.Parts[(int)SkinPart.LeftArm]),
        Vector3.Transform(new(-2, 0, 0), pose.Parts[(int)SkinPart.RightLeg]),
        Vector3.Transform(new(2, 0, 0), pose.Parts[(int)SkinPart.LeftLeg]),
    ];

    // Author review: the reported hundreds of MB came from full raster allocations on every animation frame.
    // Exercise the actual popup scene producer at 4K: its warmed per-frame allocation must remain below 64 KiB,
    // far below one raster. Retaining the first descriptor also rejects mutable-buffer reuse across queued draws.
    // This cannot establish native cache lifetime; repeated-open process measurements cover that separately.
    /// <summary>Walking updates reuse image inputs and allocate only immutable scene constants, independent of viewport area.</summary>
    [TestMethod]
    public void WalkingPresenter_UpdatesConstantsWithoutAllocatingRasters()
    {
        var snapshot = new SkinRenderSnapshot(SkinEditor.Create(SkinModel.Classic, size: 128));
        using var presenter = new SkinPreviewEffect(snapshot);
        var canvas = presenter.Surface;
        var frame = new SkinGpuFrame(snapshot, new(4096, 2160, -.4f, -.14f, 72), 63, true, false,
            new(.1f, .1f, .1f, 1), Pose: SkinAnimation.Sample(0), Antialias: true);
        var first = presenter.Create(frame);
        var original = first.ConstantBuffer.ToArray();
        var before = GC.GetAllocatedBytesForCurrentThread();
        const int frames = 60;
        for (var index = 1; index <= frames; index++)
        {
            _ = presenter.Create(frame with { Pose = SkinAnimation.Sample(index / 30.0) });
        }

        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / frames;
        Assert.IsLessThan(64L * 1024, perFrame, "A realtime frame must not allocate a viewport-sized raster.");
        Assert.AreSame(canvas, presenter.Surface);
        CollectionAssert.AreEqual(original, first.ConstantBuffer.ToArray(), "Queued draws must retain their own pose constants.");
        Assert.IsFalse(original.AsSpan().SequenceEqual(presenter.Create(frame with { Pose = SkinAnimation.Sample(.3) }).ConstantBuffer.Span),
            "Low allocation must still update the walking pose.");
    }

    /// <summary>Open previews share one canvas, which stops owning upload pixels when the final preview closes.</summary>
    [TestMethod]
    [DoNotParallelize]
    public void WalkingCanvas_IsSharedOnlyAcrossLivePreviewOwners()
    {
        // Author review: per-frame allocation checks do not detect a 16 MiB canvas rooted after every popup closes.
        var snapshot = new SkinRenderSnapshot(SkinEditor.Create(SkinModel.Classic, size: 64));
        using var first = new SkinPreviewEffect(snapshot);
        using var second = new SkinPreviewEffect(snapshot);
        var canvas = first.Surface;
        Assert.AreSame(canvas, second.Surface);
        first.Dispose();
        Assert.AreEqual(ImageStatus.Ready, canvas.Status, "another open preview still owns the canvas.");
        second.Dispose();
        Assert.AreEqual(ImageStatus.Failed, canvas.Status, "the final close must reject further upload use.");
        using var reopened = new SkinPreviewEffect(snapshot);
        Assert.AreNotSame(canvas, reopened.Surface);
        Assert.AreEqual(ImageStatus.Ready, reopened.Surface.Status);
    }

    /// <summary>Gets or sets cancellation for bounded native frame and teardown waits.</summary>
    public TestContext TestContext { get; set; }
}