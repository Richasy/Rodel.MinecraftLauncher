using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Tests.Infrastructure;

/// <summary>Serves real HTTP bodies with explicit per-file barriers after a partial response has been sent.</summary>
internal sealed class ContentDownloadServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, Transfer> _files = new();
    private readonly List<Task> _requests = [];
    private readonly Task _accept;

    internal ContentDownloadServer()
    {
        _listener.Start();
        Endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
        _accept = AcceptAsync();
    }

    internal Uri Endpoint { get; }
    internal Transfer Add(string name, byte[] bytes, bool blocked = true, int prefixLength = 64)
    {
        var transfer = new Transfer(bytes, prefixLength);
        if (!blocked) transfer.Release.TrySetResult();
        _files[name] = transfer;
        return transfer;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                _requests.Add(RespondAsync(connection));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task RespondAsync(TcpClient connection)
    {
        using (connection)
        {
            try
            {
                var stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var request = await reader.ReadLineAsync(_stop.Token);
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }

                var name = request!.Split(' ')[1].TrimStart('/');
                var transfer = _files[name];
                var bytes = transfer.Bytes;
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"), _stop.Token);
                await stream.WriteAsync(bytes.AsMemory(0, transfer.PrefixLength), _stop.Token);
                transfer.Entered.TrySetResult();
                await transfer.Release.Task.WaitAsync(_stop.Token);
                await stream.WriteAsync(bytes.AsMemory(transfer.PrefixLength), _stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (IOException) { /* A cancelled download closes its real TCP connection. */ }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await _accept;
        _listener.Dispose();
        await Task.WhenAll(_requests);
        _stop.Dispose();
    }

    internal sealed class Transfer(byte[] bytes, int prefixLength)
    {
        internal byte[] Bytes { get; } = bytes;
        internal int PrefixLength { get; } = prefixLength;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}