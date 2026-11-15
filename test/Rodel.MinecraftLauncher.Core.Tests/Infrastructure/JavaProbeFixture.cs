using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.IO.Compression;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Hosts a synthetic reporting subprocess without requiring an installed JDK; full discovery can still probe host candidates.</summary>
internal sealed class JavaProbeFixture : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Rodel.Core.Tests", "java " + Guid.NewGuid().ToString("N"));
    internal string Home => Path.Combine(Root, "jdk");
    internal string Executable => Path.Combine(Home, "bin", "java.exe");
    internal LauncherPaths Paths => new(Root);
    internal JavaDiscovery Discovery => new(Paths);
    internal int Calls => Directory.Exists(Path.Combine(Home, "calls")) ? Directory.GetFiles(Path.Combine(Home, "calls")).Length : 0;

    internal JavaProbeFixture()
    {
        var bin = Path.Combine(Home, "bin");
        Directory.CreateDirectory(bin);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "java-probe")))
            File.Copy(file, Path.Combine(bin, Path.GetFileName(file)));
        SetVersion("25.0.1");
    }

    internal void SetVersion(string version) => File.WriteAllText(Path.Combine(Home, "release"), version);

    internal string CreateArchive()
    {
        var archive = Path.Combine(Root, "java.zip");
        ZipFile.CreateFromDirectory(Home, archive);
        return archive;
    }

    internal static async Task CancelAndDrainAsync(CancellationTokenSource cancellation, params Task?[] pending)
    {
        await cancellation.CancelAsync();
        try { await Task.WhenAll(pending.OfType<Task>()); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    internal async Task<int> WaitForStartAsync(CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var marker = Path.Combine(Home, "started");
        while (!File.Exists(marker))
            await Task.Delay(20, deadline.Token);
        return int.Parse(await File.ReadAllTextAsync(marker, deadline.Token), System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static async Task CreateJunctionAsync(string link, string target, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = $"/d /c mklink /J \"{link}\" \"{target}\""
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not create the test junction.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
            throw;
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Could not create the test junction: {await output} {await error}");
        await Task.WhenAll(output, error);
    }

    /// <summary>Removes owned files after callers cancel and drain in-flight tasks; process termination is a last-resort cleanup safeguard.</summary>
    public void Dispose()
    {
        var marker = Path.Combine(Home, "started");
        if (File.Exists(marker))
        {
            var id = int.Parse(File.ReadAllText(marker), System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                using var process = Process.GetProcessById(id);
                if (!process.HasExited && process.MainModule?.FileName == Executable)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
            }
            catch (ArgumentException) { }
        }

        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}