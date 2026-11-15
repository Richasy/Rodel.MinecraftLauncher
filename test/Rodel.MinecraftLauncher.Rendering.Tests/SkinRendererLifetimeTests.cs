// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Features.Skins.Rendering;
using System.Numerics;

namespace Rodel.MinecraftLauncher.Rendering.Tests;

/// <summary>Exercises the real D3D worker's teardown, separately from Core persistence and packaged window lifecycle.</summary>
[TestClass]
public sealed class SkinRendererLifetimeTests
{
    /// <summary>Failures in attach, detach and error reporting cannot strand a peer or subsequent cleanup.</summary>
    [TestMethod]
    [DoNotParallelize]
    public async Task RecoveryCallbackFailures_ReleaseOwnershipAndKeepPeersRunning()
    {
        // Author review: a nonzero attachment failure alone never exercises exceptions inside recovery itself.
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = new SkinGpuRenderer(_ => { }, _ => ready.TrySetResult(), error => ready.TrySetException(new InvalidOperationException(error)));
        var rejected = new SkinGpuRenderer(_ => throw new InvalidOperationException("Attachment and detachment rejected."),
            _ => recovered.TrySetException(new InvalidOperationException("Rejected view submitted.")), _ =>
            {
                recovered.TrySetResult();
                throw new InvalidOperationException("Error callback rejected.");
            });
        try
        {
            peer.Submit(Frame());
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationToken);
            rejected.Submit(Frame());
            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationToken);
            ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            peer.Submit(Frame());
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationToken);
            Assert.IsFalse(rejected.HasGpuResourcesForTests);
        }
        finally
        {
            rejected.Stop();
            peer.Stop();
            await Task.WhenAll(rejected.Completion, peer.Completion).WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
        }
    }

    /// <summary>The queue has a final boundary even for work that fails outside a renderer's recovery.</summary>
    [TestMethod]
    public async Task SharedQueue_ContinuesAfterUnexpectedWorkFailure()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SkinRenderThread.Post(() => throw new InvalidOperationException("Unexpected queued failure."));
        SkinRenderThread.Post(() => ready.TrySetResult());
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
    }

    /// <summary>A hidden viewport starts a worker but never creates a device. Stopping it must complete instead of throwing on the native null context; existing skin tests never run this worker.</summary>
    [TestMethod]
    public async Task StopBeforeFirstFrame_CompletesWithoutDeviceOrCallbacks()
    {
        var callbacks = 0;
        var renderer = new SkinGpuRenderer(_ => Interlocked.Increment(ref callbacks), _ => Interlocked.Increment(ref callbacks), _ => Interlocked.Increment(ref callbacks));
        renderer.Stop();
        await renderer.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
        renderer.Stop();
        renderer.Submit(Frame());
        Assert.AreEqual(0, callbacks);
    }

    /// <summary>Wait for an actual successful frame before closing, so a passing stop test cannot merely repeat the uninitialized path.</summary>
    [TestMethod]
    public async Task StopAfterRenderedFrame_ReleasesDeviceAndCompletes()
    {
        var submitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new SkinGpuRenderer(_ => { }, _ => submitted.TrySetResult(), error => submitted.TrySetException(new InvalidOperationException(error)));
        try
        {
            renderer.Submit(Frame());
            await submitted.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationToken);
        }
        finally
        {
            renderer.Stop();
            await renderer.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
        }
    }

    /// <summary>An attachment failure occurs after native initialization, releases the device in recovery, then stopping releases again. The error callback is the barrier proving recovery ran; completion must not crash on the second release.</summary>
    [TestMethod]
    public async Task StopAfterAttachmentFailure_CompletesAfterRecoveryRelease()
    {
        var recovered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new SkinGpuRenderer(handle =>
        {
            if (handle != 0) throw new InvalidOperationException("Attachment rejected for lifetime test.");
        }, _ => recovered.TrySetException(new InvalidOperationException("Failed attachment must not submit a frame.")), error => recovered.TrySetResult(error));
        try
        {
            renderer.Submit(Frame());
            Assert.AreEqual("Attachment rejected for lifetime test.", await recovered.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationToken));
        }
        finally
        {
            renderer.Stop();
            await renderer.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
        }
    }

    private static SkinGpuFrame Frame() => new(new(SkinEditor.Create(SkinModel.Classic)), new(64, 64, 0, 0, 60), 63, true, false, Vector4.Zero);

    /// <summary>Gets or sets the runner context used to cancel bounded waits when the test run ends.</summary>
    public TestContext TestContext { get; set; }
}