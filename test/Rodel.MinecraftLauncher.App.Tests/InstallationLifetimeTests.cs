// Copyright (c) Richasy. All rights reserved.

extern alias NativeApp;
using System.Collections.Concurrent;
using InstallationLifetime = NativeApp::Rodel.MinecraftLauncher.App.Settings.InstallationLifetime;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects cooperative shutdown broadcast, installation isolation and the installed-process guard.</summary>
[TestClass]
public sealed class InstallationLifetimeTests
{
    /// <summary>
    /// Review: updater tests do not exercise an external uninstall request. Real named objects must notify
    /// every owner of one installation, leave another installation untouched, and retain the marker until
    /// the last owner releases it. These are in-process owners; real process/window exit is verified in a VM.
    /// </summary>
    [TestMethod]
    public async Task Shutdown_BroadcastsOnlyToItsInstallationAndRetainsGuardUntilLastOwnerDisposes()
    {
        var name = @"Local\RMCL.Test." + Guid.NewGuid().ToString("N");
        using var first = new InstallationLifetime(name);
        using var second = new InstallationLifetime(name);
        using var other = new InstallationLifetime(name + ".Other");
        using var received = new CountdownEvent(2);
        var unrelated = 0;
        first.Attach(static action => action(), () => received.Signal());
        second.Attach(static action => action(), () => received.Signal());
        other.Attach(static action => action(), () => Interlocked.Increment(ref unrelated));
        using var request = EventWaitHandle.OpenExisting(name + ".Close");
        request.Set();
        Assert.IsTrue(await Task.Run(() => received.Wait(TimeSpan.FromSeconds(10))), "Both owners must receive the uninstall request.");
        request.Reset();
        Assert.AreEqual(0, Volatile.Read(ref unrelated));

        first.Dispose();
        Assert.IsTrue(Mutex.TryOpenExisting(name, out var remaining), "One exiting owner must not release another owner's uninstall protection.");
        remaining.Dispose();
        second.Dispose();
        Assert.IsFalse(Mutex.TryOpenExisting(name, out var released));
        released?.Dispose();
    }

    /// <summary>
    /// Review: cancellation must cross a real queued-dispatch boundary, not just precede the signal.
    /// Hold the actual posted closure, dispose the lifetime, then drain it; it must not close a later UI.
    /// </summary>
    [TestMethod]
    public async Task Dispose_AfterExitWasQueued_PreventsStaleUiInvocation()
    {
        var name = @"Local\RMCL.Test." + Guid.NewGuid().ToString("N");
        using var lifetime = new InstallationLifetime(name);
        var queue = new ConcurrentQueue<Action>();
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exits = 0;
        lifetime.Attach(action =>
        {
            queue.Enqueue(action);
            queued.TrySetResult();
        }, () => exits++);
        using var request = EventWaitHandle.OpenExisting(name + ".Close");
        request.Set();
        await queued.Task.WaitAsync(TimeSpan.FromSeconds(10));
        lifetime.Dispose();
        while (queue.TryDequeue(out var action))
            action();
        Assert.AreEqual(0, exits);
        Assert.IsFalse(Mutex.TryOpenExisting(name, out var running));
        running?.Dispose();
    }
}
