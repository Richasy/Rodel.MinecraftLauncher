using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Serves small deterministic pack payloads over real loopback HTTP to managed or NativeAOT CLI children.</summary>
internal sealed class PackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<string, byte[]> _responses = new(StringComparer.Ordinal);
    private readonly Task _worker;
    private bool _stopped;

    internal PackHttpServer()
    {
        _listener.Start();
        Root = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/distribution/");
        _worker = ServeAsync();
    }

    internal Uri Root { get; }
    internal void Set(string relative, string content) => _responses["/distribution/" + relative] = Encoding.UTF8.GetBytes(content);

    private async Task ServeAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                using var connection = await _listener.AcceptTcpClientAsync(_lifetime.Token);
                await using var stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var request = await reader.ReadLineAsync(_lifetime.Token) ?? throw new IOException("HTTP request ended before its request line.");
                while (await reader.ReadLineAsync(_lifetime.Token) is { Length: > 0 }) { }

                var path = Uri.UnescapeDataString(request.Split(' ')[1].Split('?')[0]);
                var found = _responses.TryGetValue(path, out var bytes);
                bytes ??= [];
                var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, _lifetime.Token);
                await stream.WriteAsync(bytes, _lifetime.Token);
                await stream.FlushAsync(_lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (SocketException) when (_lifetime.IsCancellationRequested) { }
    }

    /// <summary>Stops only this fixture's listener and awaits its active request before deleting test data.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_stopped) return;
        _stopped = true;
        await _lifetime.CancelAsync();
        _listener.Dispose();
        await _worker;
        _lifetime.Dispose();
    }
}