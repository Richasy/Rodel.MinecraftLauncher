// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Crosses the real Java observation agent, native-loop transform and Core authentication without substituting the transport or observer.</summary>
[TestClass]
public sealed class GameBridgeLegacyTests
{
    /// <summary>Review: Java 17 compilation and a mock snapshot cannot detect old class-file incompatibility, the wrong XYZ triple, sampling on a worker thread, or a stale integrated server leaking remote ownership. An independent loop supplies conflicting camera/velocity/particle data and changes worlds; a live utility thread reaches the transformed client first, exposing false thread ownership and pre-eligibility throttling. The client has no diagnostic identity string: its run loop reaches Display.update through render(float), requiring bounded discovery by exact method signature. The owned-box variants serialize dimension only on a player subtype and instantiate a client subtype whose run method belongs to the base; the float variant uses FFF positioning and a six-float box. The inline-box variant persists independent Pos and Motion triples, detecting a construction-only or velocity-based guess. Double-only, root-only or runtime-class-only discovery cannot pass these shapes.</summary>
    [TestMethod]
    [DataRow("constructed")]
    [DataRow("owned")]
    [DataRow("float")]
    [DataRow("serialized")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task ObservationAgent_UsesNativeThreadAndRevokesRemoteAndReplacedWorlds(string nativeShape)
    {
        await using var game = await LegacyGame.StartAsync(nativeShape, TestContext.CancellationToken);
        var initial = await game.WaitAsync(snapshot => snapshot.Ready, TestContext.CancellationToken);
        Assert.AreEqual("independent-native-loop", initial.ClientThread);
        Assert.AreEqual("local_owned", initial.Mode); Assert.AreEqual("minecraft:overworld", initial.Dimension);
        Assert.AreEqual(12.5, initial.X); Assert.AreEqual(64, initial.Y); Assert.AreEqual(-7.25, initial.Z);
        var first = await game.Service.QueryAsync(game.Session, World(initial), TestContext.CancellationToken);
        Assert.AreEqual(Path.Combine(game.Directory, "saves", "A"), first.Data.GetProperty("directory").GetString());
        var edit = new GameEditRequest { OperationId = Guid.NewGuid().ToString("N"), GameIncarnation = initial.GameIncarnation, WorldEpoch = initial.WorldEpoch!, Dimension = initial.Dimension!, Kind = "player.setHealth", Health = 12 };
        var refused = await game.Service.EditAsync(game.Session, edit, TestContext.CancellationToken);
        Assert.AreEqual("rejected", refused.State); Assert.AreEqual("GAME_OPERATION_UNSUPPORTED", refused.ErrorCode);

        await game.CommandAsync("move", TestContext.CancellationToken);
        var moved = await game.WaitAsync(snapshot => snapshot.X == 33.125, TestContext.CancellationToken);
        Assert.AreEqual(71.5, moved.Y); Assert.AreEqual(-105.75, moved.Z); Assert.AreEqual(initial.WorldEpoch, moved.WorldEpoch);
        await game.CommandAsync("B", TestContext.CancellationToken);
        var changed = await game.WaitAsync(snapshot => snapshot.Ready && snapshot.WorldEpoch != initial.WorldEpoch, TestContext.CancellationToken);
        Assert.AreNotEqual(initial.LocalWorldEpoch, changed.LocalWorldEpoch);
        Assert.AreEqual(Path.Combine(game.Directory, "saves", "B"), (await game.Service.QueryAsync(game.Session, World(changed), TestContext.CancellationToken)).Data.GetProperty("directory").GetString());
        var stale = await Assert.ThrowsAsync<Core.Application.LauncherOperationException>(() => game.Service.QueryAsync(game.Session, World(initial), TestContext.CancellationToken));
        Assert.AreEqual("GAME_WORLD_STALE", stale.Code);

        // Reach an observed missing shared-world reference, then restore the exact same native
        // world/player objects. Old contexts must stay revoked, not resurrect when pointers match.
        await game.CommandAsync("hide", TestContext.CancellationToken);
        await game.WaitAsync(snapshot => !snapshot.Ready, TestContext.CancellationToken);
        await game.CommandAsync("restore", TestContext.CancellationToken);
        var restored = await game.WaitAsync(snapshot => snapshot.Ready && snapshot.WorldEpoch != changed.WorldEpoch, TestContext.CancellationToken);
        Assert.AreNotEqual(changed.LocalWorldEpoch, restored.LocalWorldEpoch);
        var resurrected = await Assert.ThrowsAsync<Core.Application.LauncherOperationException>(() => game.Service.QueryAsync(game.Session, World(changed), TestContext.CancellationToken));
        Assert.AreEqual("GAME_WORLD_STALE", resurrected.Code);

        // This native fixture has no End entity registration. Numeric 1 must retain its legacy identity,
        // rather than silently naming an unrelated historical sky dimension as the End.
        await game.CommandAsync("dimension", TestContext.CancellationToken);
        Assert.AreEqual("legacy:dimension_1", (await game.WaitAsync(snapshot => snapshot.Dimension == "legacy:dimension_1", TestContext.CancellationToken)).Dimension);

        await game.CommandAsync("remote", TestContext.CancellationToken);
        var remote = await game.WaitAsync(snapshot => snapshot.Mode == "remote_read_only", TestContext.CancellationToken);
        Assert.IsFalse((await game.Service.QueryAsync(game.Session, World(remote), TestContext.CancellationToken)).Data.TryGetProperty("directory", out _));
        await game.CommandAsync("menu", TestContext.CancellationToken);
        Assert.IsFalse((await game.WaitAsync(snapshot => !snapshot.Ready, TestContext.CancellationToken)).Ready);
    }

    /// <summary>Gets or sets the runner's cancellation and lifecycle context.</summary>
    public TestContext TestContext { get; set; }

    private static GameQueryRequest World(GameSnapshot snapshot) => new() { GameIncarnation = snapshot.GameIncarnation, WorldEpoch = snapshot.WorldEpoch!, Dimension = snapshot.Dimension!, Kind = "world" };

    private sealed class LegacyGame : IAsyncDisposable
    {
        private readonly LauncherPaths _paths = new(Path.Combine(Path.GetTempPath(), "Rodel.Core.Tests", "legacy-" + Guid.NewGuid().ToString("N")));
        private GameBridgeOwner? _owner;
        private Process? _process;
        private Task<string> _errors = Task.FromResult(string.Empty);
        private string _lastCommand = "initial";
        internal string Directory { get; private set; } = string.Empty;
        internal string Session { get; } = Guid.NewGuid().ToString("N");
        internal GameBridgeService Service => new(_paths);

        internal static async Task<LegacyGame> StartAsync(string nativeShape, CancellationToken ct)
        {
            var game = new LegacyGame();
            try { await game.StartCoreAsync(nativeShape, ct); return game; }
            catch { await game.DisposeAsync(); throw; }
        }

        private async Task StartCoreAsync(string nativeShape, CancellationToken ct)
        {
            _paths.EnsureDirectories();
            var instance = await new InstanceStore(_paths).CreateAsync("Observation fixture", "unmapped-original", ct);
            Directory = instance.GameDirectory;
            var version = Path.Combine(instance.MinecraftDirectory, "versions", "unmapped-original"); System.IO.Directory.CreateDirectory(version);
            var fixtures = Path.Combine(AppContext.BaseDirectory, "java-bridge-tests");
            await File.WriteAllTextAsync(Path.Combine(version, "unmapped-original.json"), """{"id":"unmapped-original","mainClass":"fixture.ClientLoop"}""", ct);
            File.Copy(Path.Combine(fixtures, "legacy-fixture.jar"), Path.Combine(version, "unmapped-original.jar"));
            var (Jar, Bindings, InputHooks) = await BridgeComponent.PrepareAsync(_paths, instance.MinecraftDirectory, instance.VersionId!, true, ct, javaMajor: 8, gameDirectory: Directory);
            using (var jar = ZipFile.OpenRead(Jar))
            {
                foreach (var entry in jar.Entries.Where(entry => entry.FullName.EndsWith(".class", StringComparison.Ordinal)))
                {
                    using var stream = entry.Open(); var header = new byte[8]; stream.ReadExactly(header);
                    Assert.IsLessThanOrEqualTo(50, (header[6] << 8) | header[7], "The packaged observation agent must execute on a Java 6 VM.");
                }
            }

            var java = Environment.GetEnvironmentVariable("RODEL_TEST_LEGACY_JAVA") ?? Environment.GetEnvironmentVariable("RODEL_TEST_JAVA8") ?? (await File.ReadAllTextAsync(Path.Combine(fixtures, "java-path.txt"), ct)).Trim();
            var start = new ProcessStartInfo(java) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, WorkingDirectory = Directory };
            start.ArgumentList.Add("-Xverify:all"); start.ArgumentList.Add("-javaagent:" + Jar);
            start.ArgumentList.Add("-cp"); start.ArgumentList.Add(Path.Combine(fixtures, "legacy-fixture.jar")); start.ArgumentList.Add("fixture.ClientLoop"); start.ArgumentList.Add(Directory); start.ArgumentList.Add(nativeShape);
            _owner = new(_paths, Session); _owner.Configure(start, Bindings, InputHooks);
            _process = Process.Start(start)!; _errors = _process.StandardError.ReadToEndAsync(CancellationToken.None); _owner.Bind(_process);
            using var monitor = Process.GetCurrentProcess();
            await JsonFiles.WriteAsync(Path.Combine(_paths.SessionsDirectory, Session + ".json"), new JsonObject
            {
                ["id"] = Session,
                ["instanceId"] = instance.Id,
                ["status"] = "running",
                ["gameBridgeEnabled"] = true,
                ["processId"] = _process.Id,
                ["processStartUtc"] = _process.StartTime.ToUniversalTime().ToString("O"),
                ["monitorId"] = Environment.ProcessId,
                ["monitorStartUtc"] = monitor.StartTime.ToUniversalTime().ToString("O")
            }, ct);
        }

        internal async Task<GameSnapshot> WaitAsync(Func<GameSnapshot, bool> predicate, CancellationToken ct)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(8));
            while (true)
            {
                if (_process!.HasExited) Assert.Fail("Observation JVM exited (" + _process.ExitCode + "): " + await _errors);
                try
                {
                    if ((await Service.GetStatusAsync(Session, deadline.Token)).State == "connected")
                    {
                        var snapshot = await Service.SnapshotAsync(Session, deadline.Token);
                        if (predicate(snapshot)) return snapshot;
                    }
                }
                catch (Core.Application.LauncherOperationException failure)
                {
                    // Keep the original Owner error and the JVM's complete stderr; a native-loop
                    // failure must not be mistaken for a transient observation or silently retried.
                    if (!_process.HasExited) { _process.Kill(entireProcessTree: true); await _process.WaitForExitAsync(CancellationToken.None); }

                    throw new InvalidOperationException("Observation request failed (" + failure.Code + ") at fixture command " + _lastCommand + ": " + failure + Environment.NewLine + "Observation JVM stderr: " + await _errors, failure);
                }

                await Task.Delay(25, deadline.Token);
            }
        }
        internal async Task CommandAsync(string command, CancellationToken ct)
        {
            await JsonFiles.WriteBytesAsync(Path.Combine(Directory, "fixture-control.txt"), System.Text.Encoding.UTF8.GetBytes(command), ct);
            _lastCommand = command;
        }

        /// <summary>Stops only the owned fixture JVM, drains Core transports and removes its isolated synthetic data.</summary>
        public async ValueTask DisposeAsync()
        {
            if (_process is { HasExited: false }) { _process.Kill(entireProcessTree: true); await _process.WaitForExitAsync(); }

            if (_owner is not null) await _owner.DisposeAsync();
            if (_process is not null) { await _errors; _process.Dispose(); }

            if (System.IO.Directory.Exists(_paths.Root)) System.IO.Directory.Delete(_paths.Root, recursive: true);
        }
    }
}