// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Features.Skins.Rendering;
using System.Numerics;

namespace Rodel.MinecraftLauncher.Rendering.Tests;

/// <summary>Protects depth-correct hover output while a static GPU scene is reused across input frames.</summary>
[TestClass]
public sealed class SkinHoverRenderingTests
{
    // Author review: checking a mirror mode or mark count misses the reported two visible cursors.
    // Actual GPU output with mirrored marks must match one front mark because the opaque cuboid
    // hides its opposite face; a separate baseline proves that the visible cursor was rendered.
    /// <summary>An isolated limb's opposite-face cursor stays occluded instead of appearing on the same front face.</summary>
    [TestMethod]
    public async Task IsolatedLimbMirror_OppositeFaceCursorIsHiddenByTheCuboid()
    {
        await using var renderer = new Readback(TestContext.CancellationToken);
        var texture = new SkinRenderSnapshot(SkinEditor.Create(SkinModel.Classic, "#C07050"));
        var surface = new SkinSurface { Part = SkinPart.RightArm, Layer = SkinLayer.Base, Face = SkinFace.Front };
        var marks = texture.BrushFootprint(new()
        {
            Tool = SkinTool.Pencil,
            Surface = surface,
            X = 0,
            Y = 5,
            Color = "#FF0000",
            Mirror = SkinMirrorMode.Body,
            MirrorParts = [SkinPart.RightArm],
        });
        var frame = new SkinGpuFrame(texture, new(240, 320, 0, 0, 28, Pivot: new(-6, 18, 0)),
            1 << (int)SkinPart.RightArm, false, false, new(.08f, .12f, .16f, 1),
            Overlay: new(texture, []), Antialias: true);
        var baseline = await renderer.Draw(frame);
        var expected = await renderer.Draw(frame with { Overlay = new(texture, [new(surface, 0, 5, 1)]) });
        var actual = await renderer.Draw(frame with { Overlay = new(texture, marks) });

        Assert.IsFalse(baseline.AsSpan().SequenceEqual(expected), "The front cursor must actually contribute pixels.");
        CollectionAssert.AreEqual(expected, actual, "The second cursor belongs behind the limb, not on the front face's other edge.");
    }

    /// <summary>Concurrent view mailboxes share one device while retaining independent pipeline state and lifetimes.</summary>
    [TestMethod]
    [DoNotParallelize]
    public async Task IndependentViews_ShareDeviceAndPreservePixelsAfterPeerSuspends()
    {
        // Author review: a single-view resume does not detect cross-view context state or duplicate devices.
        await using var model = new Readback(TestContext.CancellationToken);
        await using var atlas = new Readback(TestContext.CancellationToken);
        var texture = Texture(SkinModel.Slim, 64, 0xC07050FFu);
        var modelFrame = new SkinGpuFrame(texture, new(240, 200, -.4f, -.14f, 66), 63, true, false,
            new(.08f, .12f, .16f, 1), Overlay: new(texture, []), Antialias: true);
        var atlasFrame = modelFrame with
        {
            Camera = modelFrame.Camera with { Width = 192, Height = 224 },
            Atlas = new(2, 8, 12, 0),
            Antialias = false,
            Grid = true
        };
        var expected = await Task.WhenAll(model.Draw(modelFrame), atlas.Draw(atlasFrame));
        var device = model.Device;
        Assert.AreNotEqual(0, device);
        Assert.AreEqual(device, atlas.Device, "visible views must not allocate duplicate native devices.");
        for (var i = 0; i < 4; i++)
        {
            var actual = await Task.WhenAll(model.Draw(modelFrame), atlas.Draw(atlasFrame));
            CollectionAssert.AreEqual(expected[0], actual[0]);
            CollectionAssert.AreEqual(expected[1], actual[1]);
        }

        await model.Suspend();
        CollectionAssert.AreEqual(expected[1], await atlas.Draw(atlasFrame));
        Assert.AreEqual(device, atlas.Device, "suspending a peer cannot dispose the still-visible view's device.");
        CollectionAssert.AreEqual(expected[0], await model.Draw(modelFrame));
        Assert.AreEqual(device, model.Device, "resuming should rejoin the live device rather than duplicate it.");
    }

    /// <summary>A hidden view releases its native state and a resumed view reproduces the same complete frame.</summary>
    [TestMethod]
    public async Task Suspension_ReleasesGpuOwnershipAndResumesExactPixels()
    {
        // Author review: the existing hover comparison never releases the device between frames. This protects
        // hidden-view residency and successful resume, using an acknowledged worker boundary rather than a delay.
        await using var cached = new Readback(TestContext.CancellationToken);
        await using var complete = new Readback(TestContext.CancellationToken);
        var texture = Texture(SkinModel.Slim, 64, 0xC07050FFu);
        var frame = new SkinGpuFrame(texture, new(240, 200, -.4f, -.14f, 66), 63, true, false,
            new(.08f, .12f, .16f, 1), Overlay: new(texture, []), Antialias: true);
        var original = await Compare(frame, cached, complete);
        cached.VerifyRetainedPath(TestContext);

        await cached.Suspend();
        CollectionAssert.AreEqual(original, await Compare(frame, cached, complete),
            "resuming must retain the authored camera, texture and drawing quality.");
        cached.VerifyRetainedPath(TestContext);
    }

    // Author review: existing animation and worker-lifetime cases do not cover stale contours or retained
    // scene invalidation. An identity-pose frame uses the complete per-pixel scene path, independently of
    // the static-scene cache. Compare actual readback after hover, clear, camera/content and size changes.
    // Moving the marks must change pixels; otherwise equality against the full scene would prove nothing.
    /// <summary>Cached hover must match a complete draw, including transparency, AA and changes to its base scene.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HoverChangesAndBaseInvalidation_MatchFullScene(bool antialias)
    {
        await using var cached = new Readback(TestContext.CancellationToken);
        await using var complete = new Readback(TestContext.CancellationToken);
        var texture = Texture(SkinModel.Slim, 64, 0xC07050FFu);
        var empty = new SkinBrushOverlay(texture, []);
        var head = new SkinSurface { Part = SkinPart.Head, Layer = SkinLayer.Outer, Face = SkinFace.Front };
        var body = new SkinSurface { Part = SkinPart.Body, Layer = SkinLayer.Outer, Face = SkinFace.Front };
        var first = new SkinGpuFrame(texture, new(240, 200, -.4f, -.14f, 66), 63, true, false,
            new(.08f, .12f, .16f, 1), Overlay: empty, Antialias: antialias);
        var headFrame = first with { Overlay = new(texture, [new(head, 2, 3, 1)]) };
        var bodyFrame = first with { Overlay = new(texture, [new(body, 3, 6, 1)]) };
        var baseline = await Compare(first, cached, complete);
        cached.VerifyRetainedPath(TestContext);
        var hovered = await Compare(headFrame, cached, complete);
        Assert.IsFalse(baseline.AsSpan().SequenceEqual(hovered), "The fixture's visible head mark must reach the GPU output.");
        await Compare(bodyFrame, cached, complete);
        CollectionAssert.AreEqual(baseline, await Compare(first, cached, complete), "Leaving hover must restore the original pixels.");
        await Compare(headFrame with { Camera = first.Camera with { Yaw = .25f }, Grid = true }, cached, complete);
        var changed = Texture(SkinModel.Slim, 64, 0x40B080FFu);
        await Compare(first with { Texture = changed, Overlay = new(changed, []) }, cached, complete);
        await Compare(first with { Companion = Texture(SkinModel.Cape, 128, 0xD0B020FFu), Parts = 127 }, cached, complete);
        await Compare(headFrame with { Camera = first.Camera with { Width = 192, Height = 224 } }, cached, complete);
    }

    private static SkinRenderSnapshot Texture(SkinModel model, int size, uint color)
    {
        var document = SkinEditor.Create(model, size: size);
        Array.Fill(document.Pixels, color);
        foreach (var face in SkinLayout.Faces(model, size).Where(face => face.Surface.Layer == SkinLayer.Outer))
        {
            for (var y = face.Y; y < face.Y + face.Height; y++)
            {
                for (var x = face.X; x < face.X + face.Width; x++)
                {
                    var index = (y * size) + x;
                    document.Pixels[index] = index % 5 == 0 ? (color & 0xFFFFFF00u) | 0x80u : color;
                }
            }
        }

        return new(document);
    }

    private static async Task<byte[]> Compare(SkinGpuFrame frame, Readback cached, Readback complete)
    {
        var actual = await cached.Draw(frame);
        var identity = new SkinAnimationPose([.. Enumerable.Repeat(Matrix4x4.Identity, 7)]);
        var expected = await complete.Draw(frame with { Pose = identity });
        Assert.HasCount(expected.Length, actual);
        for (var i = 0; i < expected.Length; i++)
        {
            if (Math.Abs(expected[i] - actual[i]) > 1)
                Assert.Fail($"Full-scene mismatch at byte {i}: expected {expected[i]}, actual {actual[i]}.");
        }

        return actual;
    }

    private sealed class Readback : IAsyncDisposable
    {
        internal nint Device => _renderer.DeviceIdentityForTests;
        private readonly CancellationToken _cancellation;
        private readonly SkinGpuRenderer _renderer;
        private TaskCompletionSource<byte[]> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Readback(CancellationToken cancellation)
        {
            _cancellation = cancellation;
            _renderer = new(_ => { }, _ => { }, error => _ready.TrySetException(new InvalidOperationException(error)),
                (_, bytes, _, _) => _ready.TrySetResult(bytes));
        }

        internal async Task<byte[]> Draw(SkinGpuFrame frame)
        {
            _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _renderer.Submit(frame);
            return await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), _cancellation);
        }

        internal void VerifyRetainedPath(TestContext context)
        {
            if (_renderer.IsWarp)
            {
                context.WriteLine("WARP fallback: this run checks pixels and teardown, not the hardware retention path.");
            }
            else
            {
                Assert.IsTrue(_renderer.HasRetainedSceneForTests, "The hardware fixture must allocate its retained texture before equality can establish cache correctness.");
                context.WriteLine("Hardware retention path: real cached texture allocated before pixel comparison.");
            }
        }

        internal async Task Suspend()
        {
            await _renderer.SuspendAsync().WaitAsync(TimeSpan.FromSeconds(10), _cancellation);
            Assert.IsFalse(_renderer.HasGpuResourcesForTests, "the acknowledged hidden view must release its native ownership.");
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            // A Task.WaitAsync timer races asynchronously scheduled completion continuations under
            // whole-suite thread-pool load. A FIFO acknowledgement comes directly from the device
            // owner, after Stop's queued work, and verifies cleanup rather than merely waiting longer.
            using var drained = new ManualResetEventSlim();
            var completedAtBarrier = false;
            var releasedAtBarrier = false;
            _renderer.Stop();
            SkinRenderThread.Post(() =>
            {
                completedAtBarrier = _renderer.Completion.IsCompleted;
                releasedAtBarrier = !_renderer.HasGpuResourcesForTests;
                drained.Set();
            });
            Assert.IsTrue(drained.Wait(TimeSpan.FromSeconds(10), _cancellation), "The device owner must acknowledge the teardown queue barrier.");
            Assert.IsTrue(completedAtBarrier, "Stop must finish before subsequent device-owner work.");
            _renderer.Completion.GetAwaiter().GetResult();
            Assert.IsTrue(releasedAtBarrier, "The completed worker must release its native ownership.");
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Gets or sets cancellation for the owned render workers.</summary>
    public TestContext TestContext { get; set; } = null!;
}