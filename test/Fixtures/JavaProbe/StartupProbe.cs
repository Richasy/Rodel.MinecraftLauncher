// Copyright (c) Richasy. All rights reserved.

/// <summary>Emits controlled startup markers across an actual pipe boundary; it is not a JVM or a game window.</summary>
internal static class StartupProbe
{
    internal static async Task<int> RunAsync(string home)
    {
        await Console.Out.WriteLineAsync("[12:00:00] [main/INFO]: Loading Minecraft synthetic with Fabric Loader synthetic");
        await Console.Out.WriteLineAsync("[12:00:00] [main/INFO]: SpongePowered MIXIN Subsystem Version=synthetic");
        await Console.Out.WriteLineAsync("private-payload-must-not-be-in-startup-diagnostics");
        await Console.Out.FlushAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!File.Exists(Path.Combine(home, "startup-continue"))) await Task.Delay(20, timeout.Token);
        await Console.Out.WriteLineAsync("[12:00:01] [main/INFO]: SpongePowered MIXIN Subsystem Version=duplicate");
        await Console.Out.WriteLineAsync("[12:00:01] [main/INFO]: Created window using synthetic backend (no actual window)");
        await Console.Out.WriteLineAsync("[12:00:01] [main/INFO]: Reloading ResourceManager: synthetic");
        await Console.Out.WriteLineAsync("[12:00:01] [main/INFO]: Sound engine started");
        await Console.Out.WriteLineAsync("[12:00:01] [main/INFO]: Created: minecraft:textures/atlas/gui.png-atlas");
        return 0;
    }
}