// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Multiplayer;
using Rodel.MinecraftLauncher.Core.Servers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects the guest's actual local TCP entry across network-target changes, independently of Minecraft session recovery.</summary>
[TestClass]
public sealed class RoomGameEntryTests
{
    /// <summary>Review: status-only checks miss login, including servers with status disabled. Real streams must restore the hostname/port, retain mod suffixes and username, pass opaque bytes, then disconnect on room revocation. A 254-byte modern frame deliberately collides with legacy FE ping's first byte.</summary>
    [TestMethod]
    [DataRow("modern", true)]
    [DataRow("modern-long", true)]
    [DataRow("legacy", true)]
    [DataRow("modern", false)]
    [DataRow("modern-long", false)]
    [DataRow("legacy", false)]
    public async Task FallbackHandshake_PreservesLoginAndRemainsRevocable(string mode, bool statusAvailable)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var port = ((IPEndPoint)server.LocalEndpoint).Port;
        await using var entry = new RoomGameEntry();
        var local = ServerService.ParseAddress(entry.Address);
        var suffix = "\0FML2\0" + (mode == "modern-long" ? new string('x', 231) : string.Empty);
        var protocol = !statusAvailable ? GameStatusProtocol.Unknown : mode == "legacy" ? GameStatusProtocol.Legacy : GameStatusProtocol.Modern;
        entry.Target(server.LocalEndpoint.ToString(), "play.example.invalid", protocol);
        using var game = new TcpClient();
        await game.ConnectAsync(local.Host, local.Port, ct);
        using var remote = await server.AcceptTcpClientAsync(ct);
        var request = HandshakeWire(mode, "127.0.0.1" + suffix, local.Port);
        if (mode == "modern-long") Assert.AreEqual(0xfe, request[0]);
        await game.GetStream().WriteAsync(request, ct);
        await game.GetStream().WriteAsync(new byte[] { 0xe1 }, ct);
        var expected = HandshakeWire(mode, "play.example.invalid" + suffix, port).Concat(new byte[] { 0xe1 }).ToArray();
        var received = new byte[expected.Length];
        await remote.GetStream().ReadExactlyAsync(received, ct);
        CollectionAssert.AreEqual(expected, received);
        await remote.GetStream().WriteAsync(new byte[] { 0xf3 }, ct);
        var buffer = new byte[1];
        await game.GetStream().ReadExactlyAsync(buffer, ct);
        Assert.AreEqual(0xf3, buffer[0]);
        entry.Target(null);
        Assert.AreEqual(0, await game.GetStream().ReadAsync(buffer, ct), "Stopping the room must revoke the proxied fallback while preserving the game process.");
    }

    internal static byte[] HandshakeWire(string mode, string host, int port)
    {
        using var body = new MemoryStream();
        if (mode == "legacy")
        {
            body.Write([2, 78, 0, 6]); // Legacy protocol, six UTF-16 username characters.
            body.Write(Encoding.BigEndianUnicode.GetBytes("Player"));
            var length = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)host.Length);
            body.Write(length); body.Write(Encoding.BigEndianUnicode.GetBytes(host));
            var destination = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(destination, port);
            body.Write(destination);
            return body.ToArray();
        }

        body.Write([0, 0xff, 5]); // Handshake ID, protocol 767.
        var encoded = Encoding.UTF8.GetBytes(host);
        TestVarInt(body, encoded.Length);
        body.Write(encoded); body.Write([(byte)(port >> 8), (byte)port, 2]);
        using var packet = new MemoryStream();
        TestVarInt(packet, (int)body.Length);
        body.Position = 0; body.CopyTo(packet);
        return packet.ToArray();
    }

    private static void TestVarInt(Stream stream, int number)
    {
        do { var part = (byte)(number & 127); number >>= 7; stream.WriteByte((byte)(part | (number > 0 ? 128 : 0))); } while (number > 0);
    }

    /// <summary>Review: real accepted streams distinguish explicit route revocation from passive DNS refresh. New connections use the new endpoint, existing streams survive only a passive refresh, and room stop always closes both.</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task NetworkTargetChanges_PassiveRefreshPreservesStreams_ExplicitChangeRevokesThem(bool disconnectExisting)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        using var first = new TcpListener(IPAddress.Loopback, 0);
        using var second = new TcpListener(IPAddress.Loopback, 0);
        first.Start(); second.Start();
        await using var entry = new RoomGameEntry();
        var address = ServerService.ParseAddress(entry.Address);
        entry.Target(first.LocalEndpoint.ToString());
        using var game = new TcpClient();
        await game.ConnectAsync(address.Host, address.Port, ct);
        using var upstream = await first.AcceptTcpClientAsync(ct);
        await upstream.GetStream().WriteAsync(new byte[] { 0xe1 }, ct);
        var buffer = new byte[1];
        await game.GetStream().ReadExactlyAsync(buffer, ct);
        Assert.AreEqual(0xe1, buffer[0]);

        entry.Target(second.LocalEndpoint.ToString(), disconnectExisting: disconnectExisting);
        if (disconnectExisting)
        {
            Assert.AreEqual(0, await game.GetStream().ReadAsync(buffer, ct), "An explicit change revokes the previous route.");
        }
        else
        {
            await game.GetStream().WriteAsync(new byte[] { 0x92 }, ct);
            await upstream.GetStream().ReadExactlyAsync(buffer, ct);
            Assert.AreEqual(0x92, buffer[0], "Passive refresh must keep the existing connection on its original endpoint.");
        }

        using var reconnect = new TcpClient();
        await reconnect.ConnectAsync(address.Host, address.Port, ct);
        using var replacement = await second.AcceptTcpClientAsync(ct);
        await reconnect.GetStream().WriteAsync(new byte[] { 0xf3 }, ct);
        await replacement.GetStream().ReadExactlyAsync(buffer, ct);
        Assert.AreEqual(0xf3, buffer[0]);
        entry.Target(null);
        Assert.AreEqual(0, await reconnect.GetStream().ReadAsync(buffer, ct));
        if (!disconnectExisting) Assert.AreEqual(0, await game.GetStream().ReadAsync(buffer, ct));
    }

    /// <summary>Review: a DNS refresh must not redirect an active UDP conversation while its TCP session stays on the original server. Existing peers retain their route, new peers use the refreshed one, and explicit room stop still revokes both.</summary>
    [TestMethod]
    public async Task PassiveDatagramRefresh_RetainsExistingPeerAndRevokesAllOnStop()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        using var first = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var second = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var existing = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var newcomer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await using var entry = new RoomGameEntry();
        var local = ServerService.ParseAddress(entry.Address);
        var destination = new IPEndPoint(IPAddress.Loopback, local.Port);
        entry.Target(first.Client.LocalEndPoint!.ToString());
        await existing.SendAsync(new byte[] { 0x91 }, destination, ct);
        var original = await first.ReceiveAsync(ct);
        await first.SendAsync(original.Buffer, original.RemoteEndPoint, ct);
        CollectionAssert.AreEqual(new byte[] { 0x91 }, (await existing.ReceiveAsync(ct)).Buffer);

        entry.Target(second.Client.LocalEndPoint!.ToString(), disconnectExisting: false);
        await existing.SendAsync(new byte[] { 0x92 }, destination, ct);
        var retained = await first.ReceiveAsync(ct);
        Assert.AreEqual(original.RemoteEndPoint, retained.RemoteEndPoint);
        await first.SendAsync(retained.Buffer, retained.RemoteEndPoint, ct);
        CollectionAssert.AreEqual(new byte[] { 0x92 }, (await existing.ReceiveAsync(ct)).Buffer);
        await newcomer.SendAsync(new byte[] { 0x93 }, destination, ct);
        var added = await second.ReceiveAsync(ct);
        await second.SendAsync(added.Buffer, added.RemoteEndPoint, ct);
        CollectionAssert.AreEqual(new byte[] { 0x93 }, (await newcomer.ReceiveAsync(ct)).Buffer);

        entry.Target(null);
        await first.SendAsync(new byte[] { 0x94 }, original.RemoteEndPoint, ct);
        await second.SendAsync(new byte[] { 0x95 }, added.RemoteEndPoint, ct);
        using var absence = CancellationTokenSource.CreateLinkedTokenSource(ct);
        absence.CancelAfter(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await existing.ReceiveAsync(absence.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await newcomer.ReceiveAsync(absence.Token));
    }

    /// <summary>Review: TCP success cannot detect dropped mod datagrams. Independent UDP peers share the advertised TCP port, then replace the upstream; bytes must return to their originating peer and stale-generation replies must not leak.</summary>
    [TestMethod]
    public async Task SamePortDatagrams_IsolatePeersAndReplaceTarget()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        using var first = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var second = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var playerA = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var playerB = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await using var entry = new RoomGameEntry();
        var address = ServerService.ParseAddress(entry.Address);
        var target = new IPEndPoint(IPAddress.Loopback, address.Port);
        entry.Target(first.Client.LocalEndPoint!.ToString());
        await playerA.SendAsync(new byte[] { 0x91 }, target, ct);
        var a = await first.ReceiveAsync(ct);
        await playerB.SendAsync(new byte[] { 0x92 }, target, ct);
        var b = await first.ReceiveAsync(ct);
        Assert.AreNotEqual(a.RemoteEndPoint, b.RemoteEndPoint);
        await first.SendAsync(a.Buffer, a.RemoteEndPoint, ct);
        await first.SendAsync(b.Buffer, b.RemoteEndPoint, ct);
        CollectionAssert.AreEqual(new byte[] { 0x91 }, (await playerA.ReceiveAsync(ct)).Buffer);
        CollectionAssert.AreEqual(new byte[] { 0x92 }, (await playerB.ReceiveAsync(ct)).Buffer);

        entry.Target(second.Client.LocalEndPoint!.ToString());
        await playerA.SendAsync(new byte[] { 0x93 }, target, ct);
        var replacement = await second.ReceiveAsync(ct);
        await first.SendAsync(new byte[] { 0x94 }, a.RemoteEndPoint, ct);
        await second.SendAsync(replacement.Buffer, replacement.RemoteEndPoint, ct);
        CollectionAssert.AreEqual(new byte[] { 0x93 }, (await playerA.ReceiveAsync(ct)).Buffer);
        entry.Target(null);
        await playerA.SendAsync(new byte[] { 0x95 }, target, ct);
        using var absence = CancellationTokenSource.CreateLinkedTokenSource(ct);
        absence.CancelAfter(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await second.ReceiveAsync(absence.Token));
    }
}