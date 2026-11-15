// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Multiplayer;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects explicit end admission and normal process exit, independently of network discovery and Minecraft saving.</summary>
[TestClass]
public sealed class MultiplayerEndingTests
{
    /// <summary>Review: end can arrive while the ordinary launch API is still preparing and has not returned its session. The real launch planner, session store, room owner and end control cross that boundary; only monitor execution is held at an explicit barrier. End must cancel that exact start and finish without ever starting a network component.</summary>
    [TestMethod]
    public async Task EndDuringStart_CancelsOwnedPreparationBeforeCreatingNetwork()
    {
        using var fixture = new JavaProbeFixture();
        var host = new HeldPreparation();
        var client = new LauncherClient(fixture.Paths, sessionHost: host);
        var instance = await client.Instances.CreateAsync("Starting fixture", "1.20.1", TestContext.CancellationToken);
        await PreparationProgressTests.WriteBaseAsync(instance.MinecraftDirectory, TestContext.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var request = new MultiplayerRequest
        {
            RoomId = Guid.NewGuid().ToString("N"),
            Role = "host",
            InstanceId = instance.Id,
            AutoOpen = false,
            Launch = new() { JavaPath = fixture.Executable, Offline = true, PlayerName = "Fixture" }
        };
        var run = client.Multiplayer.RunAsync(request, cancellation.Token);
        try
        {
            await host.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.CancellationToken);
            var starting = client.Multiplayer.Get()!;
            Assert.AreEqual(host.Session, starting.SessionId, "Ownership must be recorded before monitor dispatch returns.");
            Assert.IsNull(starting.ComponentProcessId);
            await client.Multiplayer.EndAsync(request.RoomId, MultiplayerEndMode.ExitGame, TestContext.CancellationToken);
            var ended = await run.WaitAsync(TimeSpan.FromSeconds(15), TestContext.CancellationToken);
            Assert.IsFalse(ended.Active);
            Assert.AreEqual("stopped", ended.State);
            Assert.AreEqual("canceled", client.Sessions.Get(host.Session!).Status);
            Assert.IsNull(ended.ComponentProcessId);
        }
        finally { await JavaProbeFixture.CancelAndDrainAsync(cancellation, run); }
    }

    private sealed class HeldPreparation : ISessionHost
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string? Session { get; private set; }
        /// <inheritdoc/>
        public async Task StartAsync(LauncherPaths paths, string sessionId, CancellationToken cancellationToken = default)
        {
            Session = sessionId;
            Entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var file = Path.Combine(paths.SessionsDirectory, sessionId + ".json");
                var record = JsonFiles.ReadObject(file); record["status"] = "canceled";
                await JsonFiles.WriteAsync(file, record, CancellationToken.None);
                throw;
            }
        }
    }

    /// <summary>Provides cancellation for owned fixture windows and observation deadlines.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Author review: a second UI/CLI request must neither replace an accepted end nor downgrade it to a network stop.
    /// Existing admission tests cover room leases, not the pending control race. The public Core API and real atomic
    /// store are used; no room worker consumes the control, so this does not establish game or network cleanup.
    /// </summary>
    [TestMethod]
    public async Task PendingEnd_RepeatedRequestsRetainOneControl_AndCannotBeReplaced()
    {
        using var fixture = new JavaProbeFixture();
        fixture.Paths.EnsureDirectories();
        var client = new LauncherClient(fixture.Paths);
        using var owner = Process.GetCurrentProcess();
        var room = Room(owner) with { State = "connected" };
        var directory = Path.Combine(fixture.Root, "multiplayer");
        Directory.CreateDirectory(directory);
        await JsonFiles.WriteAsync(Path.Combine(directory, "room.json"), JsonSerializer.SerializeToNode(room, MultiplayerJsonContext.Default.MultiplayerRoom)!, TestContext.CancellationToken);
        await client.Multiplayer.EndAsync(room.Id, MultiplayerEndMode.ExitGame, TestContext.CancellationToken);
        var file = Path.Combine(directory, "control.json");
        var first = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
        await Task.WhenAll(client.Multiplayer.EndAsync(room.Id, MultiplayerEndMode.ExitGame, TestContext.CancellationToken),
            client.Multiplayer.EndAsync(room.Id, MultiplayerEndMode.KeepGame, TestContext.CancellationToken),
            client.Multiplayer.ControlAsync(room.Id, "retry", ct: TestContext.CancellationToken));
        CollectionAssert.AreEqual(first, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
        Assert.AreEqual("end", JsonFiles.ReadObject(file)["action"]!.GetValue<string>());
        Assert.IsFalse(owner.HasExited, "Admission must not signal a process before the owning room consumes the command.");
    }

    /// <summary>
    /// Author review: the ordinary end must target the frozen process, traverse a real native close request and wait
    /// for exit; a simulated success return cannot prove that. The existing window worker provides an independent
    /// normal-exit witness. Its optional close barrier separately proves no automatic replay or forced fallback.
    /// The child replaces Minecraft; this establishes window/process behavior, not real world persistence.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task GameExit_FrozenIdentityAndObservedWindowCompletion_WithoutForcedFallback(bool holdClose)
    {
        using var fixture = new JavaProbeFixture();
        fixture.Paths.EnsureDirectories();
        var client = new LauncherClient(fixture.Paths);
        if (holdClose) await File.WriteAllTextAsync(Path.Combine(fixture.Home, "hold-close"), string.Empty, TestContext.CancellationToken);
        var start = new ProcessStartInfo(fixture.Executable) { UseShellExecute = false };
        start.ArgumentList.Add("Synthetic.Window");
        using var child = Process.Start(start)!;
        try
        {
            await WaitAsync(() => File.Exists(Path.Combine(fixture.Home, "window-created")));
            var room = Room(child);
            await JsonFiles.WriteAsync(Path.Combine(fixture.Paths.SessionsDirectory, room.SessionId + ".json"), new JsonObject
            {
                ["id"] = room.SessionId,
                ["instanceId"] = room.InstanceId,
                ["status"] = "running",
                ["processId"] = child.Id,
                ["processStartUtc"] = room.GameStartedAt
            }, TestContext.CancellationToken);
            var hidden = await MultiplayerGameExit.AdvanceAsync(client, room, true);
            Assert.AreEqual("exit-pending", hidden.State);
            Assert.IsNull(hidden.GameCloseRequestedAt);
            Assert.IsFalse(child.HasExited);

            await File.WriteAllTextAsync(Path.Combine(fixture.Home, "show-window"), string.Empty, TestContext.CancellationToken);
            await WaitAsync(() => client.Sessions.Get(room.SessionId!).Readiness == "window_visible");
            var rejected = await MultiplayerGameExit.AdvanceAsync(client, room with { GameStartedAt = "different incarnation" }, true);
            Assert.AreEqual("MULTIPLAYER_GAME_TARGET_CHANGED", rejected.ErrorCode);
            Assert.IsFalse(File.Exists(Path.Combine(fixture.Home, "close-observed")));

            var ending = await MultiplayerGameExit.AdvanceAsync(client, hidden, true);
            Assert.IsNotNull(ending.GameCloseRequestedAt);
            await WaitAsync(() => File.Exists(Path.Combine(fixture.Home, "close-observed")));
            if (holdClose)
            {
                var pending = await MultiplayerGameExit.AdvanceAsync(client, ending with { GameCloseRequestedAt = DateTimeOffset.UtcNow.AddSeconds(-31) }, true);
                Assert.AreEqual("exit-pending", pending.State);
                Assert.AreEqual("MULTIPLAYER_GAME_EXIT_PENDING", pending.ErrorCode);
                Assert.IsFalse(child.HasExited);
                Assert.AreEqual("1", await File.ReadAllTextAsync(Path.Combine(fixture.Home, "close-observed"), TestContext.CancellationToken));
                // Reopen after the owning process disappeared: the durable accepted intent must not become a second close.
                var file = Path.Combine(fixture.Root, "multiplayer", "room.json");
                await JsonFiles.WriteAsync(file, JsonSerializer.SerializeToNode(pending with { OwnerProcessId = int.MaxValue }, MultiplayerJsonContext.Default.MultiplayerRoom)!, TestContext.CancellationToken);
                var recovered = await client.Multiplayer.EndAsync(room.Id, MultiplayerEndMode.ExitGame, TestContext.CancellationToken);
                Assert.AreEqual("exit-pending", recovered.State);
                Assert.AreEqual(pending.EndOperationId, recovered.EndOperationId);
                Assert.AreEqual(pending.GameCloseRequestedAt, recovered.GameCloseRequestedAt);
                Assert.AreEqual("1", await File.ReadAllTextAsync(Path.Combine(fixture.Home, "close-observed"), TestContext.CancellationToken));
            }
            else
            {
                await child.WaitForExitAsync(TestContext.CancellationToken);
                Assert.AreEqual(0, child.ExitCode);
                Assert.IsTrue(File.Exists(Path.Combine(fixture.Home, "normal-window-exit")));
                Assert.AreEqual("stopped", (await MultiplayerGameExit.AdvanceAsync(client, ending, true)).State);
            }
        }
        finally
        {
            // Only the owned synthetic child is force-cleaned after the blocked-close assertion.
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(CancellationToken.None);
        }
    }

    private static MultiplayerRoom Room(Process process) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Role = "host",
        Name = "End fixture",
        InstanceId = "fixture",
        Active = true,
        State = "ending",
        OwnerProcessId = Environment.ProcessId,
        OwnerStartedAt = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O"),
        SessionId = Guid.NewGuid().ToString("N"),
        GameProcessId = process.Id,
        GameStartedAt = process.StartTime.ToUniversalTime().ToString("O"),
        EndOperationId = Guid.NewGuid().ToString("N")
    };

    private async Task WaitAsync(Func<bool> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (!condition()) await Task.Delay(20, deadline.Token);
    }
}