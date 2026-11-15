using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Core.Tests.Infrastructure;

internal static class ReportStorage
{
    internal static async Task RunAsync(Func<LauncherPaths, Task> action)
    {
        var root = Path.Combine(Environment.CurrentDirectory, ".report-contract-" + Guid.NewGuid().ToString("N"));
        var paths = new LauncherPaths(root);
        paths.EnsureDirectories();
        try { await action(paths); }
        finally { Directory.Delete(root, recursive: true); }
    }
}