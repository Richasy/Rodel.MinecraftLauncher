/// <summary>A real stdout/stderr producer controlled by file barriers, not a JVM or Minecraft.</summary>
internal static class ConsoleProbe
{
    internal static async Task<int> RunAsync(string home)
    {
        await Console.Out.WriteLineAsync("[12:00:00] [main/INFO]: console-start");
        await Console.Error.WriteLineAsync("[12:00:00] [main/ERROR]: synthetic-console-error");
        await Console.Out.FlushAsync();
        await Console.Error.FlushAsync();
        File.WriteAllText(Path.Combine(home, "console-ready"), string.Empty);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var exit = Path.Combine(home, "console-exit");
        while (!File.Exists(exit)) await Task.Delay(20, timeout.Token);
        await Console.Out.WriteLineAsync("[12:00:01] [main/INFO]: console-finished");
        return int.Parse(File.ReadAllText(exit), System.Globalization.CultureInfo.InvariantCulture);
    }
}