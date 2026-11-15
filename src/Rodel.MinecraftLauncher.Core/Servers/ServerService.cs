// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Servers;

/// <summary>A parsed Java server network endpoint.</summary>
/// <param name="Host">The hostname or IP literal without bracket notation.</param>
/// <param name="Port">The TCP port selected by the address or its default.</param>
public sealed record ServerEndpoint(string Host, int Port);
/// <summary>Manages persisted favorites and performs address resolution and unauthenticated Java status queries.</summary>
public sealed class ServerService(LauncherPaths paths, Func<string, CancellationToken, Task<IReadOnlyList<ServerSrvRecord>>>? srvQuery = null)
{
    /// <summary>Resolves an address and ordered SRV candidates, retaining recoverable DNS warnings.</summary>
    public async Task<ServerResolution> ResolveAsync(string address, CancellationToken ct = default)
    {
        var endpoint = ParseAddress(address);
        var result = new ServerResolution
        {
            Address = address,
            Host = endpoint.Host,
            Port = endpoint.Port,
            ConnectHost = endpoint.Host,
            ConnectPort = endpoint.Port,
            SrvResolved = false,
            Candidates = []
        };
        // Explicit ports, including :25565, express user intent. IP literals never need SRV.
        if (IPAddress.TryParse(endpoint.Host, out _) || address.Trim().Contains(':'))
            return result;
        try
        {
            var records = await (srvQuery ?? ServerDns.QueryAsync)(endpoint.Host, ct).ConfigureAwait(false);
            if (records.Count == 1 && records[0].Target == ".")
            {
                return result with { ServiceUnavailable = true };
            }

            var candidates = ServerDns.Order(records.Where(x => x.Target.Length > 0 && x.Target != "." && x.Port is > 0 and <= 65535 && x.Priority >= 0 && x.Weight >= 0));
            if (candidates.Count > 0)
            {
                result = result with
                {
                    SrvResolved = true,
                    ConnectHost = candidates[0].Target.TrimEnd('.'),
                    ConnectPort = candidates[0].Port,
                    Candidates = [.. candidates.Select(x => new ServerCandidate
                    {
                        Host = x.Target.TrimEnd('.'),
                        Port = x.Port,
                        Priority = x.Priority,
                        Weight = x.Weight,
                        Ttl = x.Ttl
                    })]
                };
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or ArgumentException)
        {
            result = result with { SrvWarning = ex.Message };
        }

        return result;
    }

    private string FavoritesPath => Path.Combine(paths.Root, "servers.json");

    private JsonObject ReadFavorites()
    {
        var document = File.Exists(FavoritesPath) ? JsonFiles.ReadObject(FavoritesPath) : new JsonObject { ["schemaVersion"] = 1 };
        if (document["servers"] is null)
            document["servers"] = new JsonArray();
        if (document["servers"] is not JsonArray)
            throw new InvalidDataException("The server favorites document must contain a servers array.");
        return document;
    }
    /// <summary>Reads persisted favorites without rejecting or discarding unfamiliar JSON records.</summary>
    public Task<IReadOnlyList<ServerFavoriteEntry>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ServerFavoriteEntry>>([.. (ReadFavorites()["servers"] as JsonArray ?? []).Select(ServerFavoriteEntry.Read)]);
    }

    /// <summary>Reads one saved favorite including its association and unfamiliar metadata.</summary>
    public async Task<ServerFavorite> GetAsync(string id, CancellationToken ct = default)
    {
        var favorite = (await ListAsync(ct).ConfigureAwait(false)).Select(entry => entry.Favorite).FirstOrDefault(entry => entry?.Id == id)
            ?? throw new KeyNotFoundException($"Server favorite not found: {id}");
        return favorite;
    }

    /// <summary>Associates an existing instance without installing or changing its game content.</summary>
    public Task<ServerFavorite> AssociateAsync(string id, string instanceId, bool preferred = false, CancellationToken ct = default) =>
        ChangeAssociationAsync(id, instanceId, true, preferred, ct);

    /// <summary>Removes only the favorite's association. The instance and its content are retained.</summary>
    public Task<ServerFavorite> DisassociateAsync(string id, string instanceId, CancellationToken ct = default) =>
        ChangeAssociationAsync(id, instanceId, false, false, ct);

    /// <summary>Assigns a dedicated client to one server, or releases it as an ordinary local instance without moving or copying files.</summary>
    /// <param name="instanceId">The existing client instance.</param>
    /// <param name="serverId">The owning favorite, or null to release ownership while retaining ordinary associations.</param>
    /// <param name="preferred">Whether to use this client by default when connecting through the favorite.</param>
    /// <param name="ct">Cancels registration; failed favorite publication restores the previous owner.</param>
    /// <returns>The updated instance metadata, including its explicit server ownership.</returns>
    public async Task<InstanceSummary> SetClientOwnerAsync(string instanceId, string? serverId, bool preferred = false, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: FavoritesPath, cancellationToken: ct).ConfigureAwait(false);
        var instances = new InstanceStore(paths);
        var before = instances.Get(instanceId);
        if (serverId is null)
            return (await instances.SetServerOwnerAsync(instanceId, null, ct).ConfigureAwait(false)).Describe();
        var document = ReadFavorites();
        var favorite = document["servers"]!.AsArray().OfType<JsonObject>().FirstOrDefault(item => item["id"]?.GetValue<string>() == serverId)
            ?? throw new KeyNotFoundException($"Server favorite not found: {serverId}");
        if (before.ServerId is { } owner && owner != serverId)
            throw new InvalidOperationException("Release this client's current server ownership before assigning it to another server.");
        var associated = favorite["instanceIds"] as JsonArray ?? [];
        if (!associated.Any(value => value?.GetValue<string>() == instanceId))
            associated.Add((JsonNode?)JsonValue.Create(instanceId));
        if (favorite["instanceIds"] is null)
            favorite["instanceIds"] = associated;
        if (preferred || favorite["preferredInstanceId"] is null)
            favorite["preferredInstanceId"] = instanceId;
        var updated = await instances.SetServerOwnerAsync(instanceId, serverId, ct).ConfigureAwait(false);
        try
        {
            await JsonFiles.WriteAsync(FavoritesPath, document, ct).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            try
            {
                await instances.SetServerOwnerAsync(instanceId, before.ServerId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException("Server client registration and ownership restoration both failed.", failure, rollbackFailure);
            }

            throw;
        }

        return updated.Describe();
    }

    /// <summary>Atomically saves one reviewed set of instance associations and its optional preference.</summary>
    public async Task<ServerFavorite> SetInstancesAsync(string id, IReadOnlyList<string> instanceIds, string? preferredInstanceId = null, CancellationToken ct = default)
    {
        var selected = instanceIds.Distinct(StringComparer.Ordinal).ToArray();
        if (preferredInstanceId is not null && !selected.Contains(preferredInstanceId, StringComparer.Ordinal))
            throw new ArgumentException("The preferred instance must be included in the associations.", nameof(preferredInstanceId));
        var instances = new InstanceStore(paths);
        foreach (var instanceId in selected)
            _ = instances.Get(instanceId);
        using var lease = await ResourceLease.AcquireAsync(resource: FavoritesPath, cancellationToken: ct).ConfigureAwait(false);
        var document = ReadFavorites();
        var entry = document["servers"]!.AsArray().OfType<JsonObject>().FirstOrDefault(item => item["id"]?.GetValue<string>() == id)
            ?? throw new KeyNotFoundException($"Server favorite not found: {id}");
        entry["instanceIds"] = new JsonArray([.. selected.Select(value => (JsonNode?)JsonValue.Create(value))]);
        if (preferredInstanceId is null)
            entry.Remove("preferredInstanceId");
        else
            entry["preferredInstanceId"] = preferredInstanceId;
        await JsonFiles.WriteAsync(FavoritesPath, document, ct).ConfigureAwait(false);
        return ServerFavorite.Read(entry);
    }

    private async Task<ServerFavorite> ChangeAssociationAsync(string id, string instanceId, bool associate, bool preferred, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (associate)
            _ = new InstanceStore(paths).Get(instanceId);
        using var lease = await ResourceLease.AcquireAsync(resource: FavoritesPath, cancellationToken: ct).ConfigureAwait(false);
        var document = ReadFavorites();
        var entry = (document["servers"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(item => item["id"]?.GetValue<string>() == id)
            ?? throw new KeyNotFoundException($"Server favorite not found: {id}");
        var instances = entry["instanceIds"] switch
        {
            null => [],
            JsonArray values => values,
            _ => throw new InvalidDataException("The server favorite's instanceIds must be an array.")
        };
        if (instances.Any(value => value is not JsonValue item || !item.TryGetValue<string>(out _)))
            throw new InvalidDataException("The server favorite contains an invalid instance association.");
        if (associate)
        {
            if (!instances.Any(value => value!.GetValue<string>() == instanceId))
                instances.Add((JsonNode?)JsonValue.Create(instanceId));
            if (preferred)
                entry["preferredInstanceId"] = instanceId;
        }
        else
        {
            for (var index = instances.Count - 1; index >= 0; index--)
            {
                if (instances[index]!.GetValue<string>() == instanceId)
                    instances.RemoveAt(index);
            }

            if (entry["preferredInstanceId"]?.GetValue<string>() == instanceId)
                entry.Remove("preferredInstanceId");
        }

        if (entry["instanceIds"] is null)
            entry["instanceIds"] = instances;
        await JsonFiles.WriteAsync(FavoritesPath, document, ct).ConfigureAwait(false);
        return ServerFavorite.Read(entry);
    }

    /// <summary>Adds or updates a favorite without dropping other persisted metadata, and returns its saved values.</summary>
    public async Task<ServerFavorite> AddAsync(string name, string address, string? id = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ParseAddress(address);
        id ??= Guid.NewGuid().ToString("N");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var lease = await ResourceLease.AcquireAsync(resource: FavoritesPath, cancellationToken: ct).ConfigureAwait(false);
        var document = ReadFavorites();
        var servers = document["servers"] as JsonArray ?? [];
        var existing = servers.FirstOrDefault(item => item?["id"]?.GetValue<string>() == id) as JsonObject;
        var entry = existing ?? [];
        entry["id"] = id;
        entry["name"] = name;
        entry["address"] = address.Trim();
        if (existing is null)
            servers.Add((JsonNode)entry);
        if (document["servers"] is null)
            document["servers"] = servers;
        await JsonFiles.WriteAsync(FavoritesPath, document, ct).ConfigureAwait(false);
        return ServerFavorite.Read(entry);
    }

    /// <summary>Removes an existing favorite and reports the complete removed record.</summary>
    public async Task<ServerFavoriteRemoval> RemoveAsync(string id, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: FavoritesPath, cancellationToken: ct).ConfigureAwait(false);
        var document = ReadFavorites();
        var servers = document["servers"] as JsonArray ?? [];
        for (var index = 0; index < servers.Count; index++)
        {
            if (servers[index]?["id"]?.GetValue<string>() != id)
                continue;
            var store = new InstanceStore(paths);
            var dedicated = store.List().Where(instance => instance.ServerId == id).ToArray();
            var released = new List<string>();
            var removed = ServerFavorite.Read(servers[index]!.AsObject());
            servers.RemoveAt(index);
            try
            {
                foreach (var instance in dedicated)
                {
                    await store.SetServerOwnerAsync(instance.Id, null, ct).ConfigureAwait(false);
                    released.Add(instance.Id);
                }

                await JsonFiles.WriteAsync(FavoritesPath, document, ct).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                var failures = new List<Exception> { failure };
                foreach (var instanceId in released)
                {
                    try { await store.SetServerOwnerAsync(instanceId, id, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
                }

                if (failures.Count > 1)
                    throw new AggregateException("Server removal and restoration of one or more client owners failed.", failures);
                throw;
            }

            return new ServerFavoriteRemoval
            {
                Removed = true,
                Server = removed,
                ReleasedClients = released
            };
        }

        throw new KeyNotFoundException($"Server favorite not found: {id}");
    }

    /// <summary>Java Edition modern Server List Ping. Does not authenticate or join the game.</summary>
    public async Task<ServerStatus> StatusAsync(string address, int timeoutMilliseconds = 5000, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, 1);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        var token = timeout.Token;
        ServerStatus result;
        try
        {
            var resolution = await ResolveAsync(address, token).ConfigureAwait(false);
            result = new()
            {
                Address = resolution.Address,
                Host = resolution.Host,
                Port = resolution.Port,
                ConnectHost = resolution.ConnectHost,
                ConnectPort = resolution.ConnectPort,
                SrvResolved = resolution.SrvResolved,
                Candidates = resolution.Candidates,
                ServiceUnavailable = resolution.ServiceUnavailable,
                SrvWarning = resolution.SrvWarning
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ServerStatus
            {
                Address = address,
                Online = false,
                ErrorCode = "server_timeout",
                Error = "DNS resolution exceeded the status deadline."
            };
        }

        result.Online = false;
        if (result.ServiceUnavailable == true)
        {
            result.ErrorCode = "service_unavailable";
            return result;
        }

        var endpoints = result.Candidates!.Select(x => new ServerEndpoint(x.Host, x.Port)).ToArray();
        if (endpoints.Length == 0)
            endpoints = [new(result.ConnectHost!, result.ConnectPort!.Value)];
        var attempts = new List<ServerStatusAttempt>();
        for (var endpointIndex = 0; endpointIndex < endpoints.Length; endpointIndex++)
        {
            var endpoint = endpoints[endpointIndex];
            using var attemptDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            attemptDeadline.CancelAfter(Math.Max(1, timeoutMilliseconds / (endpoints.Length - endpointIndex)));
            var attemptToken = attemptDeadline.Token;
            result.ConnectHost = endpoint.Host;
            result.ConnectPort = endpoint.Port;
            result.Error = null;
            result.ErrorCode = null;
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(endpoint.Host, endpoint.Port, attemptToken).ConfigureAwait(false);
                var stream = client.GetStream();
                await using var streamDisposal = stream.ConfigureAwait(false);
                using var handshake = new MemoryStream();
                WriteVarInt(handshake, 0);
                WriteVarInt(handshake, -1);
                WriteString(handshake, result.Host!);
                handshake.WriteByte((byte)(endpoint.Port >> 8));
                handshake.WriteByte((byte)endpoint.Port);
                WriteVarInt(handshake, 1);
                await WritePacketAsync(stream, handshake.ToArray(), attemptToken).ConfigureAwait(false);
                await WritePacketAsync(stream, [0], attemptToken).ConfigureAwait(false);
                var response = await ReadPacketAsync(stream, attemptToken).ConfigureAwait(false);
                using var packet = new MemoryStream(response, false);
                if (ReadVarInt(packet) != 0)
                    throw new InvalidDataException("Server returned an unexpected status packet ID.");
                var jsonLength = ReadVarInt(packet);
                if (jsonLength < 0 || jsonLength > packet.Length - packet.Position)
                    throw new InvalidDataException("Server returned an invalid status JSON length.");
                var json = new byte[jsonLength];
                packet.ReadExactly(json);
                result.Status = JsonNode.Parse(Encoding.UTF8.GetString(json)) as JsonObject ?? throw new InvalidDataException("Server status JSON must be an object.");
                result.Online = true;
                var ping = new byte[9];
                ping[0] = 1;
                BinaryPrimitives.WriteInt64BigEndian(ping.AsSpan(1), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                var watch = Stopwatch.StartNew();
                try
                {
                    await WritePacketAsync(stream, ping, attemptToken).ConfigureAwait(false);
                    var pong = await ReadPacketAsync(stream, attemptToken).ConfigureAwait(false);
                    if (!pong.AsSpan().SequenceEqual(ping))
                        throw new InvalidDataException("Server pong did not echo the ping payload.");
                    result.LatencyMs = Math.Round(watch.Elapsed.TotalMilliseconds, 2);
                }
                catch (Exception exception) when (exception is IOException or SocketException or InvalidDataException || (exception is OperationCanceledException && !ct.IsCancellationRequested))
                {
                    result.PingWarning = exception is OperationCanceledException ? "Server returned status but did not answer ping before timeout." : exception.Message;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                result.ErrorCode = "server_timeout";
                result.Error = $"Server did not return a status response within {timeoutMilliseconds} ms.";
            }
            catch (SocketException exception)
            {
                result.ErrorCode = exception.SocketErrorCode switch
                {
                    SocketError.ConnectionRefused => "connection_refused",
                    SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "dns_failed",
                    SocketError.TimedOut => "server_timeout",
                    SocketError.NetworkUnreachable or SocketError.HostUnreachable => "network_unreachable",
                    _ => "server_connection_failed"
                };
                result.Error = exception.Message;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
            {
                result.ErrorCode = "invalid_server_response";
                result.Error = exception.Message;
            }

            attempts.Add(new() { Host = endpoint.Host, Port = endpoint.Port, Online = result.Online, ErrorCode = result.ErrorCode });
            if (result.Online || token.IsCancellationRequested)
                break;
        }

        result.Attempts = attempts;
        ct.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>Parses a hostname or IPv4/IPv6 address with an optional explicit port, defaulting to port 25565.</summary>
    public static ServerEndpoint ParseAddress(string address)
    {
        address = address.Trim();
        if (address.Length == 0)
            throw new ArgumentException("Server address is required.");
        string host;
        var port = 25565;
        if (address.StartsWith('['))
        {
            var closing = address.IndexOf(']');
            if (closing < 0 || !IPAddress.TryParse(address[1..closing], out var ip) || ip.AddressFamily != AddressFamily.InterNetworkV6)
                throw new ArgumentException("Bracketed server addresses must contain an IPv6 address.");
            host = address[1..closing];
            var suffix = address[(closing + 1)..];
            if (suffix.Length > 0 && (!suffix.StartsWith(':') || !int.TryParse(suffix[1..], out port)))
                throw new ArgumentException("Invalid server port.");
        }
        else
        {
            var colons = address.Count(character => character == ':');
            if (colons == 1)
            {
                var separator = address.LastIndexOf(':');
                host = address[..separator];
                if (!int.TryParse(address[(separator + 1)..], out port))
                    throw new ArgumentException("Invalid server port.");
            }
            else
            {
                host = address;
                if (colons > 1 && !IPAddress.TryParse(host, out _))
                    throw new ArgumentException("Use [IPv6]:port for an IPv6 server with an explicit port.");
            }
        }

        if (string.IsNullOrWhiteSpace(host) || host.Any(char.IsWhiteSpace) || host.Contains('/') || host.Contains('\\'))
            throw new ArgumentException("Invalid server hostname.");
        if (port is < 1 or > 65535)
            throw new ArgumentException("Server port must be between 1 and 65535.");
        return new(host, port);
    }

    private static async Task WritePacketAsync(Stream stream, byte[] packet, CancellationToken ct)
    {
        using var frame = new MemoryStream();
        WriteVarInt(frame, packet.Length);
        frame.Write(packet);
        await stream.WriteAsync(frame.ToArray(), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadPacketAsync(Stream stream, CancellationToken ct)
    {
        var length = await ReadVarIntAsync(stream, ct).ConfigureAwait(false);
        if (length is < 1 or > 2 * 1024 * 1024)
            throw new InvalidDataException("Server status packet length is invalid or too large.");
        var packet = new byte[length];
        await stream.ReadExactlyAsync(packet, ct).ConfigureAwait(false);
        return packet;
    }

    private static void WriteString(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        WriteVarInt(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        var remaining = (uint)value;
        do
        {
            var part = (byte)(remaining & 127);
            remaining >>= 7;
            stream.WriteByte(remaining == 0 ? part : (byte)(part | 128));
        }
        while (remaining != 0);
    }

    private static int ReadVarInt(Stream stream)
    {
        uint result = 0;
        for (var index = 0; index < 5; index++)
        {
            var value = stream.ReadByte();
            if (value < 0)
                throw new EndOfStreamException("Server closed an incomplete status packet.");
            result |= (uint)(value & 127) << (index * 7);
            if ((value & 128) == 0)
                return (int)result;
        }

        throw new InvalidDataException("Server returned an oversized VarInt.");
    }

    private static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[1];
        uint result = 0;
        for (var index = 0; index < 5; index++)
        {
            await stream.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
            result |= (uint)(buffer[0] & 127) << (index * 7);
            if ((buffer[0] & 128) == 0)
                return (int)result;
        }

        throw new InvalidDataException("Server returned an oversized VarInt.");
    }
}