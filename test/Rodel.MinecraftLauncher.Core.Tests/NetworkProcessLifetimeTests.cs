// Copyright (c) Richasy. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Verifies process-death cleanup independently of room polling or managed disposal.</summary>
[TestClass]
public sealed class NetworkProcessLifetimeTests
{
    /// <summary>Review: graceful disposal cannot expose an abandoned detached worker. Kill only the synthetic owner after a positive bootstrap/descendant barrier; both fork generations must die while an unrelated synthetic game survives.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OwnerCrash_RevokesDetachedNetworkTreeOnly(bool beforeFork)
    {
        using var fixture = new JavaProbeFixture();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var ct = deadline.Token;
        if (beforeFork) await File.WriteAllTextAsync(Path.Combine(fixture.Home, "network-hold-bootstrap"), string.Empty, ct);
        using var game = Process.Start(new ProcessStartInfo(fixture.Executable, "Synthetic.Console") { UseShellExecute = false, CreateNoWindow = true })!;
        using var owner = Process.Start(new ProcessStartInfo(fixture.Executable, "Synthetic.NetworkOwner") { UseShellExecute = false, CreateNoWindow = true })!;
        var descendants = new List<(Process Process, int Port)>();
        try
        {
            await WaitFileAsync(Path.Combine(fixture.Home, "console-ready"), ct);
            string[] roles = beforeFork ? ["bootstrap"] : ["worker", "leaf"];
            foreach (var role in roles)
            {
                var file = Path.Combine(fixture.Home, "network-" + role);
                await WaitFileAsync(file, ct);
                var lines = await File.ReadAllLinesAsync(file, ct);
                var process = Process.GetProcessById(int.Parse(lines[0], CultureInfo.InvariantCulture));
                Assert.AreEqual(lines[1], process.StartTime.ToUniversalTime().ToString("O"));
                Assert.AreEqual(fixture.Executable, process.MainModule!.FileName, true);
                var port = int.Parse(lines[2], CultureInfo.InvariantCulture);
                descendants.Add((process, port));
                if (port > 0) { using var socket = new TcpClient(); await socket.ConnectAsync("127.0.0.1", port, ct); }
            }

            owner.Kill(); // Deliberately no tree kill: Windows job ownership must do the cleanup.
            await owner.WaitForExitAsync(ct);
            foreach (var (process, _) in descendants) await process.WaitForExitAsync(ct);
            Assert.IsFalse(game.HasExited, "A room owner's failure must not kill an independently launched game.");
            Assert.IsFalse(beforeFork && File.Exists(Path.Combine(fixture.Home, "network-worker")), "A killed bootstrap cannot fork after its owner dies.");
            await File.WriteAllTextAsync(Path.Combine(fixture.Home, "console-exit"), "0", ct);
            await game.WaitForExitAsync(ct);
            Assert.AreEqual(0, game.ExitCode);
        }
        finally
        {
            if (!owner.HasExited) { owner.Kill(); await owner.WaitForExitAsync(CancellationToken.None); }

            if (!game.HasExited) { game.Kill(); await game.WaitForExitAsync(CancellationToken.None); }

            foreach (var (process, _) in descendants) { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(CancellationToken.None); } process.Dispose(); }
        }
    }

    private static async Task WaitFileAsync(string file, CancellationToken ct)
    { while (!File.Exists(file)) await Task.Delay(20, ct); }

    /// <summary>Gets or sets the test deadline source.</summary>
    public TestContext TestContext { get; set; }
}