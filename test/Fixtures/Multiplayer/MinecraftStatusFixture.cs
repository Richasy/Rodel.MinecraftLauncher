// Copyright (c) Richasy. All rights reserved.

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Rodel.MinecraftLauncher.Tests;

/// <summary>Provides independently controlled Minecraft status replies and LAN advertisements for real component and CLI checks.</summary>
internal sealed class MinecraftStatusFixture : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly Task _broadcast;
    private readonly string? _legacy;
    private bool _broadcastEnabled;
    internal MinecraftStatusFixture(int port = 0, string? legacy = null, bool broadcast = true) { _legacy = legacy; _broadcastEnabled = broadcast; _listener = new(IPAddress.Loopback, port); _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port; _loop = ServeAsync(); _broadcast = BroadcastAsync(); }
    internal int Port { get; }
    internal bool BroadcastEnabled { get => Volatile.Read(ref _broadcastEnabled); set => Volatile.Write(ref _broadcastEnabled, value); }
    private async Task BroadcastAsync()
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork) { MulticastLoopback = true };
        var target = new IPEndPoint(IPAddress.Parse("224.0.2.60"), 4445);
        var message = Encoding.UTF8.GetBytes($"[MOTD]Protocol fixture[/MOTD][AD]{Port}[/AD]");
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                if (BroadcastEnabled) await udp.SendAsync(message, target, _stop.Token);
                await Task.Delay(500, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private async Task ServeAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                try
                {
                    var stream = connection.GetStream();
                    var input = new byte[2048];
                    if (await stream.ReadAsync(input, _stop.Token) == 0) continue;
                    // The real component also probes FE after hosting; modern Minecraft still answers it.
                    var legacy = _legacy ?? (input[0] == 0xfe ? "§1\0" + "767\0" + "1.21.1\0Protocol fixture\0" + "0\0" + "8" : null);
                    if (legacy is not null)
                    {
                        if (input[0] != 0xfe) continue;
                        var text = Encoding.BigEndianUnicode.GetBytes(legacy);
                        await stream.WriteAsync(new byte[] { 0xff, (byte)(legacy.Length >> 8), (byte)legacy.Length }.Concat(text).ToArray(), _stop.Token);
                        continue;
                    }

                    var json = Encoding.UTF8.GetBytes("{\"version\":{\"name\":\"1.21.1\",\"protocol\":767},\"players\":{\"max\":8,\"online\":0},\"description\":{\"text\":\"Protocol fixture\"}}");
                    var length = Encode(json.Length);
                    byte[] response = [.. Encode(1 + length.Length + json.Length), 0, .. length, .. json];
                    await stream.WriteAsync(response, _stop.Token);
                }
                catch (IOException) { }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private static byte[] Encode(int number)
    {
        var bytes = new List<byte>();
        do { var value = (byte)(number & 127); number >>= 7; bytes.Add((byte)(value | (number > 0 ? 128 : 0))); } while (number > 0);
        return [.. bytes];
    }
    /// <summary>Stops only the fixture listener and waits for its owned loop.</summary>
    public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await Task.WhenAll(_loop, _broadcast); _listener.Dispose(); _stop.Dispose(); }
}