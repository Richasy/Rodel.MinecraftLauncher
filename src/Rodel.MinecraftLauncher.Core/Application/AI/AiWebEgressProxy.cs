// Copyright (c) Richasy. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed record AiDnsAnswer(int Type, string Data, int TTL);
internal sealed record AiDnsResponse(int Status, AiDnsAnswer[]? Answer);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AiDnsResponse))]
internal sealed partial class AiDnsJsonContext : JsonSerializerContext;

internal sealed class AiWebEgressProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentBag<Task> _connections = [];
    private readonly ConcurrentQueue<string> _failures = new();
    private readonly Dictionary<string, (DateTimeOffset Expires, IPAddress[] Addresses)> _dns = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _dnsGate = new(1, 1);
    private readonly Task _accept;
    private CancellationTokenSource? _active;
    private Dictionary<string, IPAddress[]> _privateEndpoints = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _publicHosts;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public string Username { get; } = "rodel-" + Guid.NewGuid().ToString("N");
    public string Password { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string[] Failures => [.. _failures];

    public AiWebEgressProxy()
    {
        _listener.Start(16);
        _accept = AcceptAsync();
    }

    public void BeginCall(IReadOnlyDictionary<string, IPAddress[]> privateEndpoints, CancellationToken ct, IEnumerable<string>? publicHosts = null)
    {
        if (_active is not null) throw new InvalidOperationException("Only one managed web call may own this proxy.");
        while (_failures.TryDequeue(out _)) { }

        _privateEndpoints = new Dictionary<string, IPAddress[]>(privateEndpoints, StringComparer.OrdinalIgnoreCase);
        _publicHosts = publicHosts is null ? null : new HashSet<string>(publicHosts, StringComparer.OrdinalIgnoreCase);
        _active = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        _active.CancelAfter(TimeSpan.FromSeconds(35));
    }

    public async Task EndCallAsync()
    {
        var active = _active;
        _active = null;
        if (active is not null)
        {
            await active.CancelAsync().ConfigureAwait(false);
            active.Dispose();
        }

        _privateEndpoints = new(StringComparer.OrdinalIgnoreCase);
        _publicHosts = null;
    }

    public static string Endpoint(string host, int port) => host.TrimEnd('.').ToLowerInvariant() + ":" + port;
    public static bool PrivateName(string host)
    {
        host = host.TrimEnd('.');
        return !IPAddress.TryParse(host, out _) && (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || !host.Contains('.') || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return true;
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 16)
        {
            return (bytes[0] & 0xe0) != 0x20
            || (bytes[0] == 0x20 && bytes[1] == 0x02)
            || (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
        }

        return bytes[0] is 0 or 10 or 127 || bytes[0] >= 224
            || (bytes[0] == 169 && bytes[1] == 254) || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2)
            || (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
            || (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
            || (bytes[0] == 198 && bytes[1] is 18 or 19) || address.Equals(IPAddress.Parse("168.63.129.16"));
    }

    public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal)) return [literal];
        if (PrivateName(host)) return await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        await _dnsGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_dns.TryGetValue(host, out var cached) && cached.Expires > DateTimeOffset.UtcNow) return cached.Addresses;
            using var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                ConnectCallback = async (_, token) =>
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 443), token).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch { socket.Dispose(); throw; }
                }
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            var addresses = new List<IPAddress>();
            foreach (var type in new[] { 1, 28 })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    "https://cloudflare-dns.com/dns-query?name=" + Uri.EscapeDataString(host) + "&type=" + type);
                request.Headers.Accept.ParseAdd("application/dns-json");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await response.Content.LoadIntoBufferAsync(32768, ct).ConfigureAwait(false);
                using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var dns = await JsonSerializer.DeserializeAsync(stream, AiDnsJsonContext.Default.AiDnsResponse, ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The public DNS response is empty.");
                if (dns.Status != 0) throw new HttpRequestException("The public DNS resolver did not resolve the requested host.");
                foreach (var answer in dns.Answer ?? [])
                    if (answer.Type == type && IPAddress.TryParse(answer.Data, out var ip)) addresses.Add(ip);
            }

            if (addresses.Count == 0) throw new HttpRequestException("No public DNS addresses were returned.");
            var result = addresses.Distinct().ToArray();
            _dns[host] = (DateTimeOffset.UtcNow.AddSeconds(30), result);
            return result;
        }
        finally { _dnsGate.Release(); }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                _connections.Add(HandleAsync(client));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(35));
            var stream = client.GetStream();
            try
            {
                var header = await ReadHeaderAsync(stream, deadline.Token).ConfigureAwait(false);
                var lines = header.Split("\r\n", StringSplitOptions.None);
                var first = lines[0].Split(' ', 3);
                var authorization = lines.FirstOrDefault(line => line.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase));
                var scope = _active;
                if (scope is null || first.Length != 3 || !Authenticated(authorization))
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"Rodel\"\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"), deadline.Token).ConfigureAwait(false);
                    return;
                }

                using var active = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, scope.Token);
                var connect = first[0].Equals("CONNECT", StringComparison.Ordinal);
                var uri = new Uri(connect ? "http://" + first[1] : first[1], UriKind.Absolute);
                if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
                    throw new HttpRequestException("Only HTTP(S) destinations without embedded credentials are allowed.");
                var host = uri.DnsSafeHost;
                if (_publicHosts is { } hosts && !hosts.Contains(host))
                    throw new HttpRequestException("This managed API call cannot follow a redirect or connection to another host.");
                var endpoint = Endpoint(host, uri.Port);
                IPAddress[] addresses;
                if (_privateEndpoints.TryGetValue(endpoint, out var approved))
                {
                    addresses = approved;
                }
                else
                {
                    if (PrivateName(host)) throw new HttpRequestException("A private host was not approved for this call.");
                    addresses = await ResolveAsync(host, active.Token).ConfigureAwait(false);
                    if (addresses.Any(IsPrivate)) throw new HttpRequestException("A resolved address is private, link-local or metadata.");
                }

                using var upstream = await ConnectAsync(addresses, uri.Port, active.Token).ConfigureAwait(false);
                using var target = upstream.GetStream();
                if (connect)
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"), active.Token).ConfigureAwait(false);
                }
                else
                {
                    var forwarded = new StringBuilder(first[0]).Append(' ').Append(uri.PathAndQuery).Append(" HTTP/1.1\r\n");
                    foreach (var line in lines.Skip(1).Where(line => line.Length > 0
                        && !line.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase)
                        && !line.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase)
                        && !line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)))
                    {
                        forwarded.Append(line).Append("\r\n");
                    }

                    forwarded.Append("Host: ").Append(uri.Authority).Append("\r\nConnection: close\r\n\r\n");
                    await target.WriteAsync(Encoding.ASCII.GetBytes(forwarded.ToString()), active.Token).ConfigureAwait(false);
                }

                var upload = PumpAsync(stream, target, active.Token);
                var download = PumpAsync(target, stream, active.Token);
                await Task.WhenAny(upload, download).ConfigureAwait(false);
                await active.CancelAsync().ConfigureAwait(false);
                try { await Task.WhenAll(upload, download).ConfigureAwait(false); }
                catch (OperationCanceledException) when (active.IsCancellationRequested) { }
            }
            catch (Exception error) when (error is IOException or SocketException or HttpRequestException or OperationCanceledException
                or FormatException or ObjectDisposedException or JsonException)
            {
                _failures.Enqueue(error is OperationCanceledException ? "Managed web transport canceled or exceeded its deadline." : error.Message);
                if (!deadline.IsCancellationRequested)
                {
                    try { await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\n\r\n"), deadline.Token).ConfigureAwait(false); }
                    catch (Exception write) when (write is IOException or OperationCanceledException or ObjectDisposedException) { }
                }
            }
        }
    }

    private static async Task<TcpClient> ConnectAsync(IPAddress[] addresses, int port, CancellationToken ct)
    {
        foreach (var address in addresses)
        {
            var client = new TcpClient(address.AddressFamily);
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                await client.ConnectAsync(address, port, attempt.Token).ConfigureAwait(false);
                return client;
            }
            catch (Exception error) when (error is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                client.Dispose();
            }
            catch { client.Dispose(); throw; }
        }

        ct.ThrowIfCancellationRequested();
        throw new HttpRequestException("None of the explicitly validated destination addresses accepted the connection.");
    }

    private bool Authenticated(string? header)
    {
        if (header is null) return false;
        var value = header[(header.IndexOf(':') + 1)..].Trim();
        if (!value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(value[6..]));
            var split = text.IndexOf(':');
            if (split < 0) return false;
            var user = text[..split];
            return (user == Username || user.StartsWith(Username + "__session-", StringComparison.Ordinal))
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(text[(split + 1)..]), Encoding.UTF8.GetBytes(Password));
        }
        catch (FormatException) { return false; }
    }

    private static async Task<string> ReadHeaderAsync(Stream stream, CancellationToken ct)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count < 32768)
        {
            if (await stream.ReadAsync(one, ct).ConfigureAwait(false) == 0) throw new IOException("Incomplete proxy header.");
            bytes.Add(one[0]);
            if (bytes.Count >= 4 && bytes[^4] == 13 && bytes[^3] == 10 && bytes[^2] == 13 && bytes[^1] == 10)
                return Encoding.ASCII.GetString([.. bytes]);
        }

        throw new IOException("Proxy header exceeds its bound.");
    }

    private static async Task PumpAsync(Stream source, Stream target, CancellationToken ct)
    {
        var buffer = new byte[16384];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) return;
            total += read;
            if (total > 16777216) throw new IOException("Managed web tunnel exceeds its transfer bound.");
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await EndCallAsync().ConfigureAwait(false);
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Dispose();
        await _accept.ConfigureAwait(false);
        await Task.WhenAll(_connections).ConfigureAwait(false);
        _stop.Dispose();
        _dnsGate.Dispose();
    }
}