// Copyright (c) Richasy. All rights reserved.

using System.Diagnostics;

namespace Rodel.MinecraftLauncher.App.Features.Updates;

/// <summary>Hands installation to a Windows-owned process that outlives this application, not to another product executable.</summary>
internal static class UpdateInstaller
{
    internal static async Task<Process> StartAsync(string path, string sha256, string gate)
    {
        using var parent = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(AppContext.BaseDirectory, "Assets", "Updates", "install-update.ps1"),
            "-ParentId", parent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-ParentStarted", parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-Installer", path, "-Sha256", sha256, "-Gate", gate,
        })
            start.ArgumentList.Add(argument);
        var worker = Process.Start(start) ?? throw new IOException("Windows could not start the installer handoff.");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!File.Exists(gate + ".ready"))
            {
                if (worker.HasExited)
                    throw new IOException("The installer handoff exited before it was ready.");
                await Task.Delay(50, timeout.Token);
            }

            File.Delete(gate + ".ready");
            return worker;
        }
        catch
        {
            if (!worker.HasExited)
                worker.Kill();
            worker.Dispose();
            throw;
        }
    }

    internal static void CleanupInstalled(string cache, Version current)
    {
        if (!Directory.Exists(cache))
            return;
        foreach (var directory in Directory.EnumerateDirectories(cache))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "RMCL-*-setup.exe"))
            {
                var name = Path.GetFileName(file).Split('-');
                if (name.Length != 5 || name[0] != "RMCL" || name[2] != "win"
                    || name[3] is not ("x64" or "arm64") || name[4] != "setup.exe"
                    || !GitHubUpdateClient.TryVersion(name[1], out var version) || version > current)
                    continue;
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
