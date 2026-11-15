// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Servers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Rodel.MinecraftLauncher.Core.Multiplayer;

/// <summary>Forwards same-port loopback UDP with bounded, expiring peers and isolated target generations.</summary>
internal sealed class RoomDatagrams : IAsyncDisposable
{
    private readonly UdpClient _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<(IPEndPoint Peer, Route Route), Flow> _flows = new();
    private readonly Task _receive;
    private Route? _route;

    internal RoomDatagrams(int port)
    {
        _listener = new(new IPEndPoint(IPAddress.Loopback, port));
        _receive = ReceiveAsync();
    }

    internal void Target(string? address, bool disconnectExisting = true)
    {
        var endpoint = address is null ? null : ServerService.ParseAddress(address);
        if (Equals(Volatile.Read(ref _route)?.Endpoint, endpoint)) return;
        var current = endpoint is null ? null : new Route(endpoint);
        Interlocked.Exchange(ref _route, current);
        if (!disconnectExisting && current is not null) return;
        foreach (var pair in _flows)
        {
            if (ReferenceEquals(pair.Key.Route, current)) continue;
            try { pair.Value.Stop.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    private async Task ReceiveAsync()
    {
        try
        {
            while (true)
            {
                var packet = await _listener.ReceiveAsync(_stop.Token).ConfigureAwait(false);
                var route = Volatile.Read(ref _route);
                if (route is null) continue;
                var key = (packet.RemoteEndPoint, route);
                _flows.TryGetValue(key, out var flow);
                if (flow is null)
                {
                    // Passive address refreshes affect new peers, not an existing game's UDP flow.
                    var retained = _flows.FirstOrDefault(pair => pair.Key.Peer.Equals(packet.RemoteEndPoint) && !pair.Value.Token.IsCancellationRequested);
                    if (retained.Value is not null) { key = retained.Key; flow = retained.Value; }
                }

                if (flow is null)
                {
                    if (_flows.Count >= 32) continue;
                    using var lookup = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    lookup.CancelAfter(TimeSpan.FromSeconds(5));
                    UdpClient? upstream = null;
                    try
                    {
                        var addresses = await Dns.GetHostAddressesAsync(route.Endpoint.Host, lookup.Token).ConfigureAwait(false);
                        if (addresses.Length == 0) continue;
                        var address = addresses[0];
                        upstream = new(address.AddressFamily);
                        upstream.Connect(new IPEndPoint(address, route.Endpoint.Port));
                        if (!ReferenceEquals(route, Volatile.Read(ref _route))) { upstream.Dispose(); continue; }

                        flow = new(upstream, CancellationTokenSource.CreateLinkedTokenSource(_stop.Token));
                        flow.Stop.CancelAfter(TimeSpan.FromSeconds(30));
                        _flows[key] = flow;
                        _ = ReplyAsync(key, flow);
                        if (!ReferenceEquals(route, Volatile.Read(ref _route)))
                        {
                            try { flow.Stop.Cancel(); } catch (ObjectDisposedException) { }

                            continue;
                        }
                    }
                    catch (Exception error) when (error is SocketException || (error is OperationCanceledException && !_stop.IsCancellationRequested))
                    { upstream?.Dispose(); continue; }
                }

                try
                {
                    if (Volatile.Read(ref _route) is null || flow.Token.IsCancellationRequested) continue;
                    flow.Stop.CancelAfter(TimeSpan.FromSeconds(30));
                    await flow.Socket.SendAsync(packet.Buffer, flow.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is SocketException or OperationCanceledException or ObjectDisposedException) { }
            }
        }
        catch (Exception error) when (_stop.IsCancellationRequested && error is SocketException or OperationCanceledException or ObjectDisposedException) { }
    }

    private async Task ReplyAsync((IPEndPoint Peer, Route Route) key, Flow flow)
    {
        try
        {
            while (true)
            {
                var packet = await flow.Socket.ReceiveAsync(flow.Token).ConfigureAwait(false);
                if (Volatile.Read(ref _route) is null || flow.Token.IsCancellationRequested) break;
                await _listener.SendAsync(packet.Buffer, key.Peer, flow.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is SocketException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            flow.Socket.Dispose(); flow.Stop.Dispose(); _flows.TryRemove(key, out _); flow.Done.SetResult();
        }
    }

    /// <summary>Stops accepting datagrams and drains every owned peer before releasing the shared port.</summary>
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Dispose();
        await _receive.ConfigureAwait(false);
        await Task.WhenAll(_flows.Values.Select(flow => flow.Done.Task)).ConfigureAwait(false);
        _stop.Dispose();
    }

    private sealed class Route(ServerEndpoint endpoint)
    {
        internal ServerEndpoint Endpoint { get; } = endpoint;
    }

    private sealed class Flow(UdpClient socket, CancellationTokenSource stop)
    {
        internal UdpClient Socket { get; } = socket;
        internal CancellationTokenSource Stop { get; } = stop;
        internal CancellationToken Token { get; } = stop.Token;
        internal TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}