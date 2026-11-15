using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Features.SeedExplorer.Rendering;
using Rodel.MinecraftLauncher.Tests.Fixtures;
using System.Numerics;

namespace Rodel.MinecraftLauncher.Rendering.Tests;

/// <summary>Exercises the original-model D3D worker without a window; placement and input remain separate native UIA validation.</summary>
[TestClass]
public sealed class SeedSceneRendererTests
{
    /// <summary>
    /// Review: a synthetic red original-format model independently checks geometry, atlas upload and BGRA
    /// readback. A second camera frame through the same mesh detects stale constants; Stop completion checks
    /// worker teardown. Existing skin ray-renderer cases do not exercise this triangle shader or structured buffer.
    /// </summary>
    [TestMethod]
    public async Task OriginalModelWorker_RendersTextureUpdatesCameraAndStops()
    {
        var scene = SceneFixture.Create();
        var mesh = scene.CreateMesh(52, 52, TestContext.CancellationToken);
        var first = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var worker = new SeedSceneGpuRenderer((_, pixels) => { if (Interlocked.Increment(ref calls) == 1) first.TrySetResult(pixels); else second.TrySetResult(pixels); },
            error => { first.TrySetException(new InvalidOperationException(error)); second.TrySetException(new InvalidOperationException(error)); });
        try
        {
            var camera = new SeedSceneCamera();
            worker.Submit(new(mesh, camera.Project(mesh, 256, 192), 256, 192, new Vector4(0, 0, 0, 1)));
            var pixels = await first.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.CancellationToken);
            Assert.HasCount(256 * 192 * 4, pixels);
            var center = ((96 * 256) + 128) * 4;
            Assert.IsGreaterThan((byte)180, pixels[center + 2], "The red texture must reach the center in BGRA order.");
            Assert.AreEqual((byte)0, pixels[center]);
            Assert.AreEqual((byte)0, pixels[center + 1]);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 255 }, pixels[..4]);
            worker.Submit(new(mesh, camera.Drag(30, 10).Project(mesh, 256, 192), 256, 192, new Vector4(0, 0, 0, 1)));
            var moved = await second.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.CancellationToken);
            Assert.IsFalse(pixels.SequenceEqual(moved), "A camera update must redraw the retained model mesh.");
        }
        finally { worker.Stop(); await worker.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.CancellationToken); }
    }

    /// <summary>Gets the runner's cancellation boundary.</summary>
    public TestContext TestContext { get; set; } = null!;
}