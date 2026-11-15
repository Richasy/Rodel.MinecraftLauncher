// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Servers;
using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Multiplayer;

internal static class MinecraftListener
{
    internal static async Task<int?> FindAsync(int processId, CancellationToken ct)
    {
        var candidates = TcpOwnership.Listeners().Where(item => item.Process == processId).Select(item => item.Port).Distinct().Take(64);
        var found = new List<int>();
        foreach (var port in candidates)
        {
            if (await IsGameAsync(port, ct).ConfigureAwait(false)) found.Add(port);
        }

        if (found.Count > 1) throw new Application.LauncherOperationException("MULTIPLAYER_MULTIPLE_PORTS", "More than one Minecraft listener belongs to this game; select a single LAN world.");
        return found.Count == 1 ? found[0] : null;
    }

    private static Task<bool> IsGameAsync(int port, CancellationToken ct) => CheckAsync(new("127.0.0.1", port), ct);

    internal static async Task<bool> CheckAsync(ServerEndpoint endpoint, CancellationToken ct, string? handshakeHost = null)
    {
        return await ProtocolAsync(endpoint, ct, handshakeHost).ConfigureAwait(false) != GameStatusProtocol.Unknown;
    }

    internal static async Task<GameStatusProtocol> ProtocolAsync(ServerEndpoint endpoint, CancellationToken ct, string? handshakeHost = null)
    {
        if (await ModernAsync(endpoint, handshakeHost ?? endpoint.Host, ct).ConfigureAwait(false)) return GameStatusProtocol.Modern;
        return await LegacyAsync(endpoint, ct).ConfigureAwait(false) ? GameStatusProtocol.Legacy : GameStatusProtocol.Unknown;
    }

    internal static async Task<(bool Reachable, GameStatusProtocol Protocol)> ProbeFallbackAsync(ServerEndpoint endpoint, string handshakeHost, CancellationToken ct)
    {
        var protocol = await ProtocolAsync(endpoint, ct, handshakeHost).ConfigureAwait(false);
        if (protocol != GameStatusProtocol.Unknown) return (true, protocol);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(endpoint.Host, endpoint.Port, deadline.Token).ConfigureAwait(false);
            return (true, GameStatusProtocol.Unknown);
        }
        catch (Exception error) when (error is SocketException || (error is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return (false, GameStatusProtocol.Unknown);
        }
    }

    private static async Task<bool> ModernAsync(ServerEndpoint endpoint, string handshakeHost, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(endpoint.Host, endpoint.Port, deadline.Token).ConfigureAwait(false);
            deadline.CancelAfter(TimeSpan.FromMilliseconds(700));
            var stream = tcp.GetStream();
            // Preserve the requested hostname for servers that route status requests by virtual host.
            var host = Encoding.UTF8.GetBytes(handshakeHost);
            using var handshake = new MemoryStream();
            WriteVarInt(handshake, 0);
            WriteVarInt(handshake, -1);
            WriteVarInt(handshake, host.Length);
            handshake.Write(host);
            handshake.Write([(byte)(endpoint.Port >> 8), (byte)endpoint.Port, 1]);
            using var frame = new MemoryStream();
            WriteVarInt(frame, checked((int)handshake.Length));
            handshake.Position = 0;
            handshake.CopyTo(frame);
            frame.Write([1, 0]);
            await stream.WriteAsync(frame.ToArray(), deadline.Token).ConfigureAwait(false);
            var length = await VarIntAsync(stream, deadline.Token).ConfigureAwait(false);
            if (length is < 3 or > 1024 * 1024) return false;
            var body = new byte[length];
            await stream.ReadExactlyAsync(body, deadline.Token).ConfigureAwait(false);
            if (body[0] != 0) return false;
            var offset = 1;
            var jsonLength = 0;
            for (var shift = 0; shift < 35 && offset < body.Length; shift += 7)
            {
                var value = body[offset++];
                jsonLength |= (value & 127) << shift;
                if ((value & 128) == 0) break;
            }

            if (jsonLength != body.Length - offset) return false;
            using var document = JsonDocument.Parse(body.AsMemory(offset));
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("version", out _) && document.RootElement.TryGetProperty("description", out _);
        }
        catch (Exception error) when (error is IOException or SocketException or JsonException || (error is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return false;
        }
    }

    private static async Task<bool> LegacyAsync(ServerEndpoint endpoint, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(endpoint.Host, endpoint.Port, deadline.Token).ConfigureAwait(false);
            deadline.CancelAfter(TimeSpan.FromMilliseconds(700));
            var stream = tcp.GetStream();
            // 1.3 accepts the FE ping; 1.4–1.6 additionally recognize the 01 payload.
            await stream.WriteAsync(new byte[] { 0xfe, 0x01 }, deadline.Token).ConfigureAwait(false);
            var header = new byte[3];
            await stream.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
            var characters = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(1));
            if (header[0] != 0xff || characters is 0 or > 4096) return false;
            var bytes = new byte[characters * 2];
            await stream.ReadExactlyAsync(bytes, deadline.Token).ConfigureAwait(false);
            var text = new UnicodeEncoding(true, false, true).GetString(bytes);
            var fields = text.StartsWith("§1\0", StringComparison.Ordinal) ? text.Split('\0') : text.Split('§');
            if (fields.Length < 3) return false;
            if (text.StartsWith("§1\0", StringComparison.Ordinal) && (fields.Length != 6 || !Number(fields[1]) || fields[2].Length == 0)) return false;
            return Number(fields[^2]) && Number(fields[^1]);
        }
        catch (Exception error) when (error is IOException or SocketException or DecoderFallbackException || (error is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return false;
        }
    }

    private static bool Number(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 0;

    private static void WriteVarInt(Stream stream, int number)
    {
        var value = unchecked((uint)number);
        do
        {
            var part = (byte)(value & 127);
            value >>= 7;
            stream.WriteByte(value == 0 ? part : (byte)(part | 128));
        } while (value != 0);
    }

    private static async Task<int> VarIntAsync(Stream stream, CancellationToken ct)
    {
        var result = 0;
        var buffer = new byte[1];
        for (var shift = 0; shift < 35; shift += 7)
        {
            await stream.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
            result |= (buffer[0] & 127) << shift;
            if ((buffer[0] & 128) == 0) return result;
        }

        throw new InvalidDataException("Invalid Minecraft status frame.");
    }
}

internal enum GameStatusProtocol { Unknown, Modern, Legacy }