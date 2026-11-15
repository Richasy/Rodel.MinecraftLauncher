// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Multiplayer;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Exercises real status sockets, without treating an arbitrary open TCP port as a Minecraft world.</summary>
[TestClass]
public sealed class MinecraftListenerTests
{
    /// <summary>Review: modern-only probing loses manually opened 1.3–1.6 worlds. Independent wire replies cover both legacy encodings, modern routing and malformed services; no JVM compatibility is inferred.</summary>
    [TestMethod]
    [DataRow("modern", true)]
    [DataRow("§aOld world§2§8", true)]
    [DataRow("§1\0" + "78\0" + "1.6.4\0Legacy world\0" + "2\0" + "8", true)]
    [DataRow("§1\0bad\0version\0world\0" + "2\0" + "8", false)]
    [DataRow("HTTP/1.1 200 OK", false)]
    public async Task StatusWire_RecognizesOnlyGameReplies(string response, bool expected)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(8));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var peer = await listener.AcceptTcpClientAsync(stop.Token);
                    var stream = peer.GetStream();
                    var first = new byte[1];
                    await stream.ReadExactlyAsync(first, stop.Token);
                    if (response == "modern")
                    {
                        var handshake = new byte[first[0]];
                        await stream.ReadExactlyAsync(handshake, stop.Token);
                        await stream.ReadExactlyAsync(new byte[2], stop.Token); // Consume the separate status request before closing.
                        Assert.AreEqual(9, handshake[6]);
                        Assert.AreEqual("localhost", Encoding.UTF8.GetString(handshake, 7, 9), "The requested virtual host must reach the server.");
                        var json = "{\"version\":{},\"description\":\"World\"}"u8.ToArray();
                        await stream.WriteAsync(new byte[] { (byte)(json.Length + 2), 0, (byte)json.Length }.Concat(json).ToArray(), stop.Token);
                        return;
                    }

                    if (first[0] != 0xfe) continue; // Old servers close unsupported modern handshakes.
                    await stream.ReadExactlyAsync(new byte[1], stop.Token);
                    var text = Encoding.BigEndianUnicode.GetBytes(response);
                    var header = new byte[3];
                    header[0] = 0xff;
                    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(1), checked((ushort)response.Length));
                    await stream.WriteAsync(header.Concat(text).ToArray(), stop.Token);
                    return;
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }, stop.Token);
        try { Assert.AreEqual(expected, await MinecraftListener.CheckAsync(new("localhost", port), stop.Token)); await serve; }
        finally { stop.Cancel(); await serve; }
    }

    /// <summary>Review: a silent endpoint must not hold cancellation until the next room poll. The accepted connection is the barrier, rather than an assumed timing delay.</summary>
    [TestMethod]
    public async Task SilentEndpoint_CallerCancellationIsPreserved()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var probe = MinecraftListener.CheckAsync(new("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port), stop.Token);
        using var accepted = await listener.AcceptTcpClientAsync(TestContext.CancellationToken);
        stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await probe);
    }

    /// <summary>Review: an explicitly supplied fallback may disable status without disabling login. Real sockets distinguish reachability from Minecraft identity; strict host discovery must still reject the same status-less service.</summary>
    [TestMethod]
    public async Task FallbackWithoutStatus_AllowsReachableAddressWithoutClaimingGameIdentity()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new Servers.ServerEndpoint("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
        var serve = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var connection = await listener.AcceptTcpClientAsync(stop.Token);
                    _ = await connection.GetStream().ReadAsync(new byte[256], stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }, stop.Token);
        try
        {
            Assert.IsFalse(await MinecraftListener.CheckAsync(endpoint, stop.Token));
            var (Reachable, Protocol) = await MinecraftListener.ProbeFallbackAsync(endpoint, endpoint.Host, stop.Token);
            Assert.IsTrue(Reachable);
            Assert.AreEqual(GameStatusProtocol.Unknown, Protocol);
        }
        finally { stop.Cancel(); await serve; }
    }

    /// <summary>Gets or sets the test deadline source.</summary>
    public TestContext TestContext { get; set; }
}