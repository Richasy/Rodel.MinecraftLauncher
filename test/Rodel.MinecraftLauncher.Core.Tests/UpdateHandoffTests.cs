// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Updates;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Exercises the shipped Windows handoff script with real processes; only the interactive installer is substituted.</summary>
[TestClass]
public sealed class UpdateHandoffTests
{
    /// <summary>
    /// Review: HTTP tests cannot detect tray/close vetoes or a helper that installs before shutdown.
    /// The probe records whether its parent is alive and waits on a completion marker, so both the
    /// post-exit launch and deletion-after-installer-exit boundaries are independently observable.
    /// </summary>
    [TestMethod]
    [DataRow("install")]
    [DataRow("no-close-authorization")]
    [DataRow("tampered-after-download")]
    public async Task Handoff_RequiresActualExitAuthorizationAndVerifiedBytes(string scenario)
    {
        var directory = Path.Combine(Path.GetTempPath(), "rmcl-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "update-probe")))
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        var installer = Path.Combine(directory, "RMCL-0.2611.5.0-win-x64-setup.exe");
        File.Copy(Path.Combine(directory, "update-probe.exe"), installer);
        var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(installer)));
        var parentStart = new ProcessStartInfo(Path.Combine(directory, "update-probe.exe"))
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        parentStart.ArgumentList.Add("--parent");
        using var parent = Process.Start(parentStart)!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        Process? worker = null;
        try
        {
            Assert.AreEqual("ready", await parent.StandardOutput.ReadLineAsync(deadline.Token));
            File.WriteAllText(Path.Combine(directory, "parent-id"), parent.Id.ToString(CultureInfo.InvariantCulture));
            var gate = installer + ".install";
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[]
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(AppContext.BaseDirectory, "update-handoff.ps1"),
                "-ParentId", parent.Id.ToString(CultureInfo.InvariantCulture), "-ParentStarted", parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                "-Installer", installer, "-Sha256", digest, "-Gate", gate,
            })
            {
                start.ArgumentList.Add(argument);
            }

            worker = Process.Start(start)!;
            await WaitForFileAsync(gate + ".ready", deadline.Token);
            Assert.IsFalse(File.Exists(Path.Combine(directory, "installer-started")));
            if (scenario != "no-close-authorization")
                File.WriteAllText(gate, string.Empty);
            if (scenario == "tampered-after-download")
                File.AppendAllText(installer, "tampered");
            parent.StandardInput.Close();
            await parent.WaitForExitAsync(deadline.Token);
            if (scenario == "install")
            {
                await WaitForFileAsync(Path.Combine(directory, "installer-started"), deadline.Token);
                Assert.AreEqual("parent-exited", File.ReadAllText(Path.Combine(directory, "installer-started")));
                Assert.IsTrue(File.Exists(installer), "A running wizard must retain its download.");
                File.WriteAllText(Path.Combine(directory, "finish-installer"), string.Empty);
                await worker.WaitForExitAsync(deadline.Token);
                Assert.IsTrue(File.Exists(Path.Combine(directory, "installer-finished")));
                Assert.IsFalse(File.Exists(installer), "A completed wizard must release and delete its download.");
                Assert.AreEqual(0, worker.ExitCode);
            }
            else
            {
                await worker.WaitForExitAsync(deadline.Token);
                Assert.IsFalse(File.Exists(Path.Combine(directory, "installer-started")));
                Assert.IsTrue(File.Exists(installer), "A refused handoff must not run or erase the retained download.");
                Assert.AreNotEqual(0, worker.ExitCode);
            }
        }
        finally
        {
            if (!parent.HasExited) { parent.Kill(); await parent.WaitForExitAsync(CancellationToken.None); }

            if (worker is not null)
            {
                if (!worker.HasExited) { worker.Kill(entireProcessTree: true); await worker.WaitForExitAsync(CancellationToken.None); }

                worker.Dispose();
            }

            Directory.Delete(directory, true);
        }
    }

    /// <summary>Review: recovery after an interrupted helper must delete only installed versions inside owned cache entries, never future offers or unrelated files.</summary>
    [TestMethod]
    public void StartupCleanup_PreservesNewerAndUnownedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "rmcl-cleanup-" + Guid.NewGuid().ToString("N"));
        var owned = Path.Combine(root, Guid.NewGuid().ToString("N"));
        var unrelated = Path.Combine(root, "unrelated");
        Directory.CreateDirectory(owned);
        Directory.CreateDirectory(unrelated);
        var old = Path.Combine(owned, "RMCL-0.2611.4.0-win-x64-setup.exe");
        var future = Path.Combine(owned, "RMCL-0.2611.6.0-win-arm64-setup.exe");
        var other = Path.Combine(unrelated, "RMCL-0.2611.4.0-win-x64-setup.exe");
        foreach (var path in new[] { old, future, other })
            File.WriteAllText(path, "retained bytes");
        try
        {
            UpdateInstaller.CleanupInstalled(root, new(0, 2611, 5, 0));
            Assert.IsFalse(File.Exists(old));
            Assert.AreEqual("retained bytes", File.ReadAllText(future));
            Assert.AreEqual("retained bytes", File.ReadAllText(other));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task WaitForFileAsync(string path, CancellationToken cancellationToken)
    {
        while (!File.Exists(path))
            await Task.Delay(20, cancellationToken);
    }

    /// <summary>Gets the test deadline for fixture processes.</summary>
    public TestContext TestContext { get; set; }
}