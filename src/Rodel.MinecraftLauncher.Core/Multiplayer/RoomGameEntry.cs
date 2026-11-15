// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Servers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Rodel.MinecraftLauncher.Core.Multiplayer;

/// <summary>Keeps a guest's loopback game entry stable when its owned network component changes ports.</summary>
internal sealed class RoomGameEntry : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots = new(32, 32);
    private readonly ConcurrentDictionary<long, (CancellationTokenSource Stop, TaskCompletionSource Done)> _connections = new();
    private readonly Task _accept;
    private readonly RoomDatagrams _datagrams;
    private EntryTarget? _target;
    private long _next;
    internal string Address { get; }

    internal RoomGameEntry()
    {
        // TCP and UDP need the same advertised port; retry if another UDP listener already owns it.
        for (var attempt = 0; ; attempt++)
        {
            _listener.Start();
            try { _datagrams = new(((IPEndPoint)_listener.LocalEndpoint).Port); break; }
            catch (SocketException error) when (error.SocketErrorCode == SocketError.AddressAlreadyInUse && attempt < 7) { _listener.Stop(); }
            catch { _listener.Stop(); _stop.Dispose(); _slots.Dispose(); throw; }
        }

        Address = "127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accept = AcceptAsync();
    }

    internal void Target(string? address, string? handshakeHost = null, GameStatusProtocol protocol = GameStatusProtocol.Modern, bool disconnectExisting = true)
    {
        var target = address is null ? null : new EntryTarget(ServerService.ParseAddress(address), handshakeHost, protocol);
        if (Equals(Interlocked.Exchange(ref _target, target), target)) return;
        _datagrams.Target(address, disconnectExisting);
        if (!disconnectExisting && target is not null) return;
        // TCP streams cannot be spliced into a new Minecraft session. Close the old transport;
        // the player's ordinary reconnect uses this same stable entry, with no second game process.
        foreach (var (Stop, _) in _connections.Values)
        {
            try { Stop.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                await _slots.WaitAsync(_stop.Token).ConfigureAwait(false);
                TcpClient incoming;
                try { incoming = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false); }
                catch { _slots.Release(); throw; }

                var identity = Interlocked.Increment(ref _next);
                var stop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _connections[identity] = (stop, done);
                _ = OwnConnectionAsync(identity, incoming, Volatile.Read(ref _target), stop, done);
            }
        }
        catch (Exception error) when (_stop.IsCancellationRequested && error is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private async Task OwnConnectionAsync(long identity, TcpClient incoming, EntryTarget? target, CancellationTokenSource stop, TaskCompletionSource done)
    {
        try { await ForwardAsync(incoming, target, stop.Token).ConfigureAwait(false); }
        finally
        {
            stop.Dispose(); _slots.Release(); _connections.TryRemove(identity, out _); done.SetResult();
        }
    }

    private static async Task ForwardAsync(TcpClient incoming, EntryTarget? target, CancellationToken ct)
    {
        using (incoming)
        {
            if (target is null) return;
            using var outgoing = new TcpClient();
            using var transfer = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connect.CancelAfter(TimeSpan.FromSeconds(5));
                    await outgoing.ConnectAsync(target.Endpoint.Host, target.Endpoint.Port, connect.Token).ConfigureAwait(false);
                }

                if (target.HandshakeHost is { } host)
                    await GameHandshake.ForwardAsync(incoming.GetStream(), outgoing.GetStream(), host, target.Endpoint.Port, target.Protocol, transfer.Token).ConfigureAwait(false);
                var fromGame = incoming.GetStream().CopyToAsync(outgoing.GetStream(), transfer.Token);
                var toGame = outgoing.GetStream().CopyToAsync(incoming.GetStream(), transfer.Token);
                await Task.WhenAny(fromGame, toGame).ConfigureAwait(false);
                transfer.Cancel();
                await Task.WhenAll(fromGame, toGame).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    /// <summary>Stops the room's loopback listener and awaits its bounded set of active streams.</summary>
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Stop();
        await _accept.ConfigureAwait(false);
        await _datagrams.DisposeAsync().ConfigureAwait(false);
        await Task.WhenAll(_connections.Values.Select(connection => connection.Done.Task)).ConfigureAwait(false);
        _listener.Dispose(); _slots.Dispose(); _stop.Dispose();
    }

    private sealed record EntryTarget(ServerEndpoint Endpoint, string? HandshakeHost, GameStatusProtocol Protocol);
}