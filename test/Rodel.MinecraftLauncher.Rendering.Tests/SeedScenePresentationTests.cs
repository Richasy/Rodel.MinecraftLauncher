using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Features.SeedExplorer.Rendering;
using Rodel.MinecraftLauncher.Tests.Fixtures;
using Sprout.Interop;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using TerraFX.Interop.DirectX;

namespace Rodel.MinecraftLauncher.Rendering.Tests;

/// <summary>Checks the production composition path independently of the opt-in pixel readback renderer.</summary>
[TestClass]
public sealed class SeedScenePresentationTests
{
    /// <summary>
    /// Review: a producer that only runs the readback callback, recreates its swap chain on resize, or releases a
    /// caller-retained surface would pass the texture test but break the viewport. Actual DXGI present counts,
    /// dimensions and a retained COM reference exercise those boundaries; this does not claim DWM visibility.
    /// </summary>
    [TestMethod]
    public async Task CompositionRenderer_PresentsResizesAndStopsWithRetainedSurface()
    {
        var scene = SceneFixture.Create();
        var mesh = scene.CreateMesh(52, 52, TestContext.CancellationToken);
        var attached = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = Channel.CreateUnbounded<SeedSceneFrame>();
        var attaches = 0;
        nint retained = 0;
        var worker = new SeedSceneGpuRenderer(handle =>
        {
            Marshal.AddRef(handle);
            Interlocked.Increment(ref attaches);
            if (Interlocked.CompareExchange(ref retained, handle, 0) == 0) attached.TrySetResult(handle);
            else Marshal.Release(handle);
        }, frame => frames.Writer.TryWrite(frame), error =>
        {
            var failure = new InvalidOperationException(error);
            attached.TrySetException(failure); frames.Writer.TryComplete(failure);
        });
        nint surface = 0;
        try
        {
            var camera = new SeedSceneCamera();
            var first = new SeedSceneFrame(mesh, camera.Project(mesh, 256, 192), 256, 192, new Vector4(0, 0, 0, 1));
            worker.Submit(first);
            surface = await attached.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationToken);
            Assert.AreSame(first, await frames.Reader.ReadAsync(TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationToken));
            var (Presents, Width, Height) = Inspect(surface);
            Assert.IsGreaterThan(0u, Presents);
            Assert.AreEqual((256u, 192u), (Width, Height));
            var second = new SeedSceneFrame(mesh, camera.Drag(30, 10).Project(mesh, 320, 224), 320, 224, new Vector4(0, 0, 0, 1));
            worker.Submit(second);
            Assert.AreSame(second, await frames.Reader.ReadAsync(TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationToken));
            var resized = Inspect(surface);
            Assert.IsGreaterThan(Presents, resized.Presents);
            Assert.AreEqual((320u, 224u), (resized.Width, resized.Height));
            Assert.AreEqual(1, attaches, "Resize must retain the composition surface already attached by the caller.");
            worker.Stop();
            await worker.Completion.WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationToken);
            Assert.AreEqual(resized, Inspect(surface), "The caller's COM reference remains valid after the producer stops.");
        }
        finally
        {
            worker.Stop();
            await worker.Completion.WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationToken);
            var release = Interlocked.Exchange(ref retained, 0);
            if (release != 0) Marshal.Release(release);
        }
    }

    private static unsafe (uint Presents, uint Width, uint Height) Inspect(nint surface)
    {
        var chain = (IDXGISwapChain1*)surface;
        uint count; chain->GetLastPresentCount(&count).ThrowIfFailed();
        DXGI_SWAP_CHAIN_DESC1 description; chain->GetDesc1(&description).ThrowIfFailed();
        return (count, description.Width, description.Height);
    }

    /// <summary>Gets the runner's cancellation boundary.</summary>
    public TestContext TestContext { get; set; } = null!;
}