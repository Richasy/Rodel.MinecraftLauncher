// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Multiplayer;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Windows.Win32;
using Windows.Win32.System.Threading;

/// <summary>Forks actual detached network descendants under the production Windows job owner.</summary>
internal static class NetworkOwnerProbe
{
    internal static async Task<int> OwnerAsync(string home)
    {
        using var job = NetworkProcessJob.Start(Environment.ProcessPath!, Path.Combine(home, "network-port"), home);
        await WaitAsync(Path.Combine(home, "network-owner-exit"));
        return 0;
    }

    internal static async Task<int> BootstrapAsync(string home)
    {
        Mark(home, "bootstrap", 0);
        if (File.Exists(Path.Combine(home, "network-hold-bootstrap"))) await WaitAsync(Path.Combine(home, "network-allow-fork"));
        Detached("--network-worker");
        return 0;
    }

    internal static async Task<int> WorkerAsync(string home, bool leaf)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        if (!leaf) Detached("--network-leaf");
        Mark(home, leaf ? "leaf" : "worker", ((IPEndPoint)listener.LocalEndpoint).Port);
        await WaitAsync(Path.Combine(home, "network-worker-exit"));
        return 0;
    }

    private static unsafe void Detached(string mode)
    {
        var command = $"\"{Environment.ProcessPath}\" {mode}\0".ToCharArray().AsSpan();
        var startup = new STARTUPINFOW { cb = (uint)sizeof(STARTUPINFOW) };
        if (!PInvoke.CreateProcess(Environment.ProcessPath, ref command, null, null, false, PROCESS_CREATION_FLAGS.DETACHED_PROCESS,
            null, AppContext.BaseDirectory, startup, out var process))
        {
            throw new IOException("Could not start detached network fixture.");
        }

        _ = PInvoke.CloseHandle(process.hThread);
        _ = PInvoke.CloseHandle(process.hProcess);
    }

    private static void Mark(string home, string role, int port)
    {
        using var process = Process.GetCurrentProcess();
        var file = Path.Combine(home, "network-" + role);
        File.WriteAllText(file + ".tmp", $"{process.Id}\n{process.StartTime.ToUniversalTime():O}\n{port}");
        File.Move(file + ".tmp", file);
    }

    private static async Task WaitAsync(string file)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (!File.Exists(file)) await Task.Delay(20, deadline.Token);
    }
}