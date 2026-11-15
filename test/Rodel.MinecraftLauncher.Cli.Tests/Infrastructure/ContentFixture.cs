using System.IO.Compression;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Identifies the synthetic instance, source archive, and content directory owned by a content scenario.
/// </summary>
internal sealed record ContentFixture(string InstanceId, string Source, string Mods)
{
    internal static async Task<ContentFixture> CreateAsync(CliSandbox cli, CancellationToken cancellationToken)
    {
        var created = (await cli.RunAsync("instance", "create", "content-fixture", "--version", "synthetic-version")).ReadEnvelope(0);
        var id = created["data"]!["id"]!.GetValue<string>();
        var mods = Path.Combine(created["data"]!["gameDirectory"]!.GetValue<string>(), "mods");
        Directory.CreateDirectory(mods);
        await File.WriteAllTextAsync(Path.Combine(mods, "notes.txt"), "unselected", cancellationToken);
        var sources = Path.Combine(cli.Root, "sources", id);
        Directory.CreateDirectory(sources);
        var source = Path.Combine(sources, "selected.jar");
        WritePackage(source, "synthetic package, not a runnable mod");
        return new(id, source, mods);
    }

    internal static void WritePackage(string path, string marker)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("marker.txt").Open());
        writer.Write(marker);
    }
}