namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Places the reporting worker in a CLI sandbox; the sandbox owns all files and the CLI harness owns its processes.</summary>
internal sealed class JavaProbeFixture
{
    internal string Home { get; }
    internal string Executable => Path.Combine(Home, "bin", "java.exe");
    internal int Calls => Directory.Exists(Path.Combine(Home, "calls")) ? Directory.GetFiles(Path.Combine(Home, "calls")).Length : 0;

    internal JavaProbeFixture(string root)
    {
        Home = Path.Combine(root, "jdk");
        var bin = Path.Combine(Home, "bin");
        Directory.CreateDirectory(bin);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "java-probe")))
            File.Copy(file, Path.Combine(bin, Path.GetFileName(file)));
        SetVersion("25.0.1");
    }

    internal void SetVersion(string version) => File.WriteAllText(Path.Combine(Home, "release"), version);
}