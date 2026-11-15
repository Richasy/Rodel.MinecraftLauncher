// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Rodel.MinecraftLauncher.Core.Multiplayer;

internal sealed class TerracottaClient(int port) : IDisposable
{
    private readonly HttpClient _http = new(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(5) };
    internal int Port => port;
    internal TerracottaMeta? Meta { get; private set; }
    internal int WorkerId { get; private set; }
    internal string WorkerStartedAt { get; private set; } = string.Empty;
    private string _temporaryRoot = string.Empty;
    private NetworkProcessJob? _job;
    internal bool Alive
    {
        get
        {
            try { using var worker = Process.GetProcessById(WorkerId); return !worker.HasExited && worker.StartTime.ToUniversalTime().ToString("O") == WorkerStartedAt; }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
        }
    }

    internal static async Task<TerracottaClient> StartAsync(string roomDirectory, CancellationToken ct)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Assets", "Terracotta");
        var executable = Path.Combine(directory, "terracotta.exe");
        var expected = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "74c10568a7fea9c1d38cf8d2d4ca90baf1517f8e5a26c63d3349db70bc449796",
            System.Runtime.InteropServices.Architecture.Arm64 => "782c2fa911488d487447694acca6b17fa68304c87023fb6814b83a167fc2845f",
            _ => throw new PlatformNotSupportedException("This architecture has no bundled multiplayer component.")
        };
        var stream = File.OpenRead(executable);
        await using (stream.ConfigureAwait(false))
        {
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false)) != expected)
                throw new LauncherOperationException("MULTIPLAYER_COMPONENT_INVALID", "The bundled multiplayer component failed verification.");
        }

        executable = await TerracottaRuntime.PrepareAsync(directory, ct).ConfigureAwait(false);
        directory = Path.GetDirectoryName(executable)!;
        Directory.CreateDirectory(roomDirectory);
        var rendezvous = Path.Combine(roomDirectory, "port-" + Guid.NewGuid().ToString("N") + ".json");
        // Upstream extracts native helpers with ordinary Win32 paths. A deep launcher/test root
        // can exceed MAX_PATH before a helper is started. Keep its private temp short while the
        // canonical data-root hash preserves isolation between launcher installations.
        var rootKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ResourceLease.CanonicalPath(roomDirectory).ToUpperInvariant())))[..20];
        var temporary = Path.Combine(Path.GetTempPath(), "rmcl-net", rootKey);
        Directory.CreateDirectory(temporary);
        // Isolate the upstream's temp-based single-instance rendezvous to this data root.
        // Never reuse, reset or terminate a component owned by another launcher.
        var job = NetworkProcessJob.Start(executable, rendezvous, temporary);
        var transferred = false;
        // Finish the bounded bootstrap handshake even if submission is canceled, so a forked
        // worker can be positively identified and closed rather than abandoned without a port.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try
        {
            while (!File.Exists(rendezvous))
                await Task.Delay(100, deadline.Token).ConfigureAwait(false);
            var value = JsonSerializer.Deserialize(await File.ReadAllTextAsync(rendezvous, deadline.Token).ConfigureAwait(false), TerracottaJsonContext.Default.TerracottaBootstrap);
            if (value?.Port is not > 0 or > 65535)
                throw new InvalidDataException("Invalid component port.");
            var client = new TerracottaClient(value.Port);
            try
            {
                var pid = TcpOwnership.Listeners().Where(item => item.Port == value.Port).Select(item => item.Process).Distinct().Single();
                using var process = Process.GetProcessById(pid);
                if (!job.Contains(process)) throw new IOException("The component endpoint is outside this room's network process tree.");
                if (!string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                {
                    var ownerFile = File.OpenRead(process.MainModule?.FileName ?? throw new IOException("Component owner path is unavailable."));
                    await using var ownerDisposal = ownerFile.ConfigureAwait(false);
                    if (Convert.ToHexStringLower(await SHA256.HashDataAsync(ownerFile, deadline.Token).ConfigureAwait(false)) != expected)
                        throw new IOException("Component endpoint ownership does not match a verified executable.");
                }

                client.WorkerId = process.Id;
                client.WorkerStartedAt = process.StartTime.ToUniversalTime().ToString("O");
                client._temporaryRoot = temporary;
                client._job = job;
                client.Meta = await client.ReadAsync("meta", TerracottaJsonContext.Default.TerracottaMeta, deadline.Token).ConfigureAwait(false);
                if (client.Meta.Version.TrimStart('v') != "0.4.2")
                    throw new InvalidDataException("Unexpected multiplayer component version.");
                if (ct.IsCancellationRequested)
                {
                    await client.StopAsync(deadline.Token).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                }

                transferred = true;
                return client;
            }
            catch { client.KillOwnedWorker(); client.Dispose(); throw; }
        }
        finally
        {
            if (File.Exists(rendezvous)) File.Delete(rendezvous);
            if (!transferred) job.Dispose();
        }
    }

    internal Task<TerracottaState> StateAsync(CancellationToken ct) => ReadAsync("state", TerracottaJsonContext.Default.TerracottaState, ct);
    internal Task PrepareEngineAsync(CancellationToken ct) => TerracottaRuntime.RedirectEngineAsync(_temporaryRoot, WorkerId, DateTimeOffset.Parse(WorkerStartedAt, System.Globalization.CultureInfo.InvariantCulture), ct);
    internal static async Task StopRecordedAsync(int port, int workerId, string startedAt, CancellationToken ct)
    {
        try
        {
            using var worker = Process.GetProcessById(workerId);
            if (worker.HasExited || worker.StartTime.ToUniversalTime().ToString("O") != startedAt)
                return;
            if (!TcpOwnership.Listeners().Contains((port, workerId)))
                throw new IOException("The recorded component no longer owns its endpoint.");
            using var client = new TerracottaClient(port) { WorkerId = workerId, WorkerStartedAt = startedAt };
            await client.StopAsync(ct).ConfigureAwait(false);
        }
        catch (ArgumentException) { }
    }
    internal Task BeginAsync(string role, string code, string player, CancellationToken ct) => SendAsync("state/" + (role == "host" ? "scanning" : "guesting") + "?room=" + Uri.EscapeDataString(code) + "&player=" + Uri.EscapeDataString(player), ct);
    internal Task ResetAsync(CancellationToken ct) => SendAsync("state/ide", ct);
    internal async Task StopAsync(CancellationToken ct)
    {
        try
        {
            await SendAsync("panic?peaceful=true", ct).ConfigureAwait(false);
            if (WorkerId != 0)
            {
                using var worker = Process.GetProcessById(WorkerId);
                if (worker.StartTime.ToUniversalTime().ToString("O") == WorkerStartedAt)
                    await worker.WaitForExitAsync(ct).ConfigureAwait(false);
            }
        }
        catch (ArgumentException) { }
        finally { KillOwnedWorker(); _job?.Dispose(); _job = null; }
    }

    private void KillOwnedWorker()
    {
        if (WorkerId == 0) return;
        try
        {
            using var worker = Process.GetProcessById(WorkerId);
            if (!worker.HasExited && worker.StartTime.ToUniversalTime().ToString("O") == WorkerStartedAt)
                worker.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    internal async Task<int> HostPortAsync(CancellationToken ct)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, Meta!.ScaffoldingPort, ct).ConfigureAwait(false);
        var stream = tcp.GetStream();
        var command = Encoding.ASCII.GetBytes("c:server_port");
        var request = new byte[5 + command.Length];
        request[0] = checked((byte)command.Length);
        command.CopyTo(request, 1);
        await stream.WriteAsync(request, ct).ConfigureAwait(false);
        var header = new byte[5];
        await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        if (header[0] != 0 || BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1)) != 2)
            throw new IOException("The host did not confirm its game port.");
        var body = new byte[2];
        await stream.ReadExactlyAsync(body, ct).ConfigureAwait(false);
        return BinaryPrimitives.ReadUInt16BigEndian(body);
    }

    private async Task<T> ReadAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var disposal = source.ConfigureAwait(false);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        for (var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false); read != 0; read = await source.ReadAsync(buffer, ct).ConfigureAwait(false))
        {
            if (body.Length + read > 1024 * 1024) throw new InvalidDataException("Component response exceeded its bounded limit.");
            body.Write(buffer, 0, read);
        }

        return JsonSerializer.Deserialize(body.GetBuffer().AsSpan(0, checked((int)body.Length)), type) ?? throw new InvalidDataException("Empty component response.");
    }

    private async Task SendAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Releases the HTTP transport and owned network job without stopping any game process.</summary>
    public void Dispose() { _job?.Dispose(); _job = null; _http.Dispose(); }
}