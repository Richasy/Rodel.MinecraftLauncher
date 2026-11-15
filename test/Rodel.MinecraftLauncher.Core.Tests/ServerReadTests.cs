using Rodel.MinecraftLauncher.Core.Servers;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Windows.Win32;
using Windows.Win32.NetworkManagement.Dns;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects server read compatibility at the persisted-favorite and actual TCP status boundaries.</summary>
[TestClass]
public sealed class ServerReadTests
{
    /// <summary>
    /// DnsQuery_W metadata exposes an ANSI record alias, but its actual strings are UTF-16. An independently
    /// constructed wide record chain catches accidental PSTR decoding or reading the wrong union/TTL fields.
    /// This tests the native-result projection without depending on a public DNS service or freeing stack memory.
    /// </summary>
    [TestMethod]
    public unsafe void SrvNativeRecords_WideAlias_PreservesTargetAndRecordFields()
    {
        const string target = "\u670d\u52a1.example.";
        fixed (char* name = target)
        {
            var srv = new DNS_RECORDW { wType = (ushort)PInvoke.DNS_TYPE_SRV, dwTtl = 300 };
            srv.Data.SRV = new() { pNameTarget = new(name), wPort = 25566, wPriority = 3, wWeight = 7 };
            var other = new DNS_RECORDW { pNext = &srv };
            var result = ServerDns.ReadRecords((DNS_RECORDA*)&other);
            Assert.HasCount(1, result);
            Assert.AreEqual(new ServerSrvRecord(target, 25566, 3, 7, 300), result[0]);
        }
    }

    /// <summary>
    /// Reading favorites must not drop unknown array entries or normalize noncanonical fields.
    /// The existing suites do not cover favorites. Comparing the actual saved array and file bytes
    /// distinguishes a tolerant typed projection from one that filters records or rewrites player-owned metadata.
    /// </summary>
    [TestMethod]
    public async Task List_UnfamiliarFavoriteRecords_PreservesDataWithoutRewritingStorage()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            const string document = """
                {"schemaVersion":1,"servers":[
                  {"id":"favorite","name":"Local","address":"127.0.0.1:25565","vendor":null},
                  null,7,["unknown"],{"id":null,"name":123,"address":{"nested":true}}]}
                """;
            var file = Path.Combine(paths.Root, "servers.json");
            await File.WriteAllTextAsync(file, document, TestContext.CancellationToken);
            var entries = await new ServerService(paths).ListAsync(TestContext.CancellationToken);
            Assert.HasCount(5, entries);
            Assert.AreEqual("favorite", entries[0].Favorite!.Id);
            Assert.AreEqual("127.0.0.1:25565", entries[0].Favorite!.Address);
            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(document)!["servers"],
                JsonSerializer.SerializeToNode(entries, ServerJsonContext.Default.IReadOnlyListServerFavoriteEntry)));
            Assert.AreEqual(document, await File.ReadAllTextAsync(file, TestContext.CancellationToken));
        });
    }

    /// <summary>
    /// A real loopback status exchange must preserve the server's open response and the original handshake hostname after SRV.
    /// This adds protocol evidence absent from the current suites, not model-accessor coverage. An echoed ping proves the
    /// latency path completed; explicit null for a successful attempt's errorCode and absent outer errors are legacy wire contracts.
    /// </summary>
    [TestMethod]
    public async Task Status_SrvLoopbackResponse_PreservesOpenPayloadAndSuccessfulAttempt()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            const string status = """{"description":{"text":"local"},"players":{"online":2},"vendor":[null,{"future":true}]}""";
            var responder = RespondAsync(listener, status, deadline.Token);
            try
            {
                var service = new ServerService(paths, (_, _) => Task.FromResult<IReadOnlyList<ServerSrvRecord>>(
                    [new("127.0.0.1.", port, 0, 0, 60)]));
                var result = await service.StatusAsync("synthetic.invalid", 5000, deadline.Token);
                var handshake = await responder;
                Assert.IsTrue(Encoding.UTF8.GetString(handshake).Contains("synthetic.invalid", StringComparison.Ordinal),
                    "SRV must change the network endpoint, not the virtual hostname in the Java handshake.");
                Assert.IsTrue(result.Online);
                Assert.AreEqual("127.0.0.1", result.ConnectHost);
                Assert.AreEqual(port, result.ConnectPort);
                Assert.IsTrue(result.SrvResolved);
                Assert.IsNotNull(result.LatencyMs);
                Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(status), result.Status));
                var wire = JsonSerializer.SerializeToNode(result, ServerJsonContext.Default.ServerStatus)!.AsObject();
                Assert.IsFalse(wire.ContainsKey("errorCode"));
                Assert.IsFalse(wire.ContainsKey("pingWarning"));
                var attempt = wire["attempts"]![0]!.AsObject();
                Assert.IsTrue(attempt.ContainsKey("errorCode"));
                Assert.IsNull(attempt["errorCode"]);
                Assert.IsTrue(attempt["online"]!.GetValue<bool>());
            }
            finally
            {
                deadline.Cancel();
                listener.Stop();
                try { await responder; }
                catch (Exception error) when (error is OperationCanceledException or SocketException or IOException) { }
            }
        });
    }

    private static async Task<byte[]> RespondAsync(TcpListener listener, string status, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        using var stream = client.GetStream();
        var handshake = await ReadShortFrameAsync(stream, ct);
        var request = await ReadShortFrameAsync(stream, ct);
        CollectionAssert.AreEqual(new byte[] { 0 }, request);
        var json = Encoding.UTF8.GetBytes(status);
        // The synthetic response and all packets in this fixture fit a one-byte VarInt.
        var response = new byte[json.Length + 3];
        response[0] = checked((byte)(json.Length + 2));
        response[1] = 0;
        response[2] = checked((byte)json.Length);
        json.CopyTo(response, 3);
        await stream.WriteAsync(response, ct);
        var ping = await ReadShortFrameAsync(stream, ct);
        Assert.HasCount(9, ping);
        Assert.AreEqual(1, ping[0]);
        await stream.WriteAsync(new byte[] { (byte)ping.Length }, ct);
        await stream.WriteAsync(ping, ct);
        return handshake;
    }

    private static async Task<byte[]> ReadShortFrameAsync(Stream stream, CancellationToken ct)
    {
        var prefix = new byte[1];
        await stream.ReadExactlyAsync(prefix, ct);
        Assert.IsLessThan(128, prefix[0], "Fixture packets must fit a single-byte VarInt.");
        var bytes = new byte[prefix[0]];
        await stream.ReadExactlyAsync(bytes, ct);
        return bytes;
    }

    /// <summary>Gets or sets the test context supplying cancellation for persisted-favorite reads.</summary>
    public TestContext TestContext { get; set; }
}