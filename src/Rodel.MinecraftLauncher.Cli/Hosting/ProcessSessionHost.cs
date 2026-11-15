// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class ProcessSessionHost(string executable, IReadOnlyList<string>? prefix) : ISessionHost
{
    /// <summary>Starts a detached CLI monitor for an existing game session without waiting for the game to exit.</summary>
    /// <param name="paths">The data root containing the persisted session record.</param>
    /// <param name="sessionId">The recorded session to monitor; this does not create or launch a game.</param>
    /// <param name="cancellationToken">Cancels before monitor creation; it does not own the detached monitor's lifetime.</param>
    /// <returns>A completed task after the monitor process has been created.</returns>
    public Task StartAsync(LauncherPaths paths, string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in prefix ?? [])
            start.ArgumentList.Add(argument);
        foreach (var token in LauncherCommands.CommandPath(LauncherCommands.Hosting.SessionHost))
            start.ArgumentList.Add(token);
        start.ArgumentList.Add(sessionId);
        start.ArgumentList.Add(CliOptions.DataDirectory.Name);
        start.ArgumentList.Add(paths.Root);
        using var monitor = Process.Start(start) ?? throw new IOException("Session monitor did not start.");
        return Task.CompletedTask;
    }
}