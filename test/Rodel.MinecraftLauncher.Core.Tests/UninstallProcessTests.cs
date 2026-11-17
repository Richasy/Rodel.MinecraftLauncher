// Copyright (c) Richasy. All rights reserved.

using System.Diagnostics;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Exercises the shipped uninstall worker against only real, test-owned processes.</summary>
[TestClass]
public sealed class UninstallProcessTests
{
    /// <summary>
    /// Review: handoff tests wait for voluntary exit, so cannot prove uninstall forces blocked processes
    /// to exit or protects another directory's same-name executable. Keep stdin open after each ready
    /// signal, then require forced exit of both targets and continued execution of the prefix sibling.
    /// </summary>
    [TestMethod]
    public async Task StopInstalledApp_ForcesAllExactPathInstancesAndPreservesSameNameSibling()
    {
        var root = Path.Combine(Path.GetTempPath(), "rmcl-uninstall-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "目标 app");
        var sibling = target + "-copy";
        var name = "rmcl-uninstall-" + Guid.NewGuid().ToString("N") + ".exe";
        var children = new List<Process>();
        Process? worker = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            PrepareProbe(target, name);
            PrepareProbe(sibling, name);
            foreach (var directory in new[] { target, target, sibling })
            {
                var start = new ProcessStartInfo(Path.Combine(directory, name))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                };
                start.ArgumentList.Add("--parent");
                var child = Process.Start(start)!;
                children.Add(child);
                Assert.AreEqual("ready", await child.StandardOutput.ReadLineAsync(deadline.Token));
                Assert.IsFalse(child.CloseMainWindow(), "The probe has no window that could perform cooperative exit.");
            }

            var stop = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[]
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-File", Path.Combine(AppContext.BaseDirectory, "stop-installed-app.ps1"),
                "-ExecutablePath", Path.Combine(target, name).ToUpperInvariant(),
            })
                stop.ArgumentList.Add(argument);
            worker = Process.Start(stop)!;
            var errors = worker.StandardError.ReadToEndAsync(deadline.Token);
            await worker.WaitForExitAsync(deadline.Token);
            Assert.AreEqual(0, worker.ExitCode, await errors);
            foreach (var child in children.Take(2))
            {
                await child.WaitForExitAsync(deadline.Token);
                Assert.AreNotEqual(0, child.ExitCode, "The probe must be killed, not released through stdin.");
            }

            Assert.IsFalse(children[2].HasExited, "A same-name process in a prefix-similar directory must survive.");
        }
        finally
        {
            if (worker is not null)
            {
                if (!worker.HasExited) { worker.Kill(entireProcessTree: true); await worker.WaitForExitAsync(CancellationToken.None); }
                worker.Dispose();
            }

            foreach (var child in children)
            {
                if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(CancellationToken.None); }
                child.Dispose();
            }

            await DeleteProbeFilesAsync(root);
        }
    }

    private static async Task DeleteProbeFilesAsync(string directory)
    {
        var elapsed = Stopwatch.StartNew();
        while (Directory.Exists(directory))
        {
            try { Directory.Delete(directory, true); }
            catch (Exception error) when (elapsed.Elapsed < TimeSpan.FromSeconds(3)
                && error is IOException or UnauthorizedAccessException)
            {
                // Windows can release the killed apphost's image mapping just after process exit is signalled.
                await Task.Delay(20, CancellationToken.None);
            }
        }
    }

    private static void PrepareProbe(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "update-probe")))
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        File.Copy(Path.Combine(directory, "update-probe.exe"), Path.Combine(directory, name));
    }

    /// <summary>Gets the test cancellation deadline used to bound the owned processes.</summary>
    public TestContext TestContext { get; set; }
}
