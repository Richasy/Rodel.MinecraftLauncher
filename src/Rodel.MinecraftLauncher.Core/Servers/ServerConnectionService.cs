// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Servers;

/// <summary>Bounded client-log connection evidence, independent of process existence and server-list status.</summary>
public sealed class ServerConnectionReport
{
    /// <summary>Gets the frozen requested server address.</summary>
    public required string Server { get; init; }
    /// <summary>Gets the actual attributed connection observation.</summary>
    public string Observation { get; set; } = "unknown";
    /// <summary>Gets the classified connection issue.</summary>
    public string IssueCode { get; set; } = "unknown";
    /// <summary>Gets the observed source or none.</summary>
    public string Source { get; set; } = "none";
    /// <summary>Gets explicit uncertainty about world entry.</summary>
    public bool? Joined { get; }
    /// <summary>Gets the limits of interpreting missing or bounded client logs.</summary>
    public string Interpretation { get; } = "Client log evidence is not proof of world entry. Missing, truncated or unsupported logs leave the connection unknown.";
    /// <summary>Gets or sets the matching or differing attempt's actual event position.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AttemptEventIndex { get; set; }
    /// <summary>Gets or sets bounded original evidence; an observed different target retains explicit null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Evidence { get; set { field = value; NullField("evidence", value is null); } }
    /// <summary>Gets or sets the attributed logger, including explicit null after a new attempt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Logger { get; set { field = value; NullField("logger", value is null); } }
    /// <summary>Gets or sets the original timestamp value without coercing caller-provided parser metadata.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Timestamp { get; set { field = value; NullField("timestamp", value is null); } }
    /// <summary>Gets or sets bounded distinct registry identifiers observed after the current attempt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? RegistryEntries { get; set; }
    /// <summary>Gets or sets the source session for an explicit read.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SessionId { get; set; }
    /// <summary>Gets or sets the independently observed process lifecycle status, including explicit null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcessStatus { get; set { field = value; NullField("processStatus", value is null); } }
    /// <summary>Gets or sets actual game-directory ownership.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? GameActive { get; set; }
    /// <summary>Gets or sets the actual per-stream cursor/truncation observations.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, LogStreamSummary>? LogRead { get; set; }
    /// <summary>Gets or sets actual log-read warnings.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Warnings { get; set; }
    /// <summary>Gets or sets whether bounded logs were actually read.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Bounded { get; set; }
    /// <summary>Gets or sets an independent read failure without claiming the connection failed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReadError { get; set; }
    /// <summary>Gets explicit nulls and future source fields while distinguishing unobserved fields from observed absence.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    private void NullField(string name, bool present)
    {
        if (present) AdditionalData[name] = JsonSerializer.SerializeToElement((string?)null, ServerConnectionJsonContext.Default.String);
        else AdditionalData.Remove(name);
    }
}

/// <summary>NativeAOT metadata for client-connection evidence and explicit-null fields.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ServerConnectionReport))]
[JsonSerializable(typeof(string))]
public sealed partial class ServerConnectionJsonContext : JsonSerializerContext;

/// <summary>Reports bounded client-log evidence, independently of process and SLP status.</summary>
public sealed class ServerConnectionService(LauncherPaths paths)
{
    /// <summary>Reads bounded original client-log evidence for the frozen target while retaining independent log read failures.</summary>
    public async Task<ServerConnectionReport> ReadAsync(string sessionId, CancellationToken ct = default)
    {
        var sessions = new SessionService(paths);
        var session = sessions.Get(sessionId);
        var address = session.Server ?? throw new ArgumentException("The session has no server target.", nameof(sessionId));
        var gameActive = session.GameDirectory is { } game && sessions.HasActiveGame(game);
        var report = Analyze(address, []);
        try
        {
            var logs = await new LogEventService(paths).ReadAsync(sessionId, budget: 262144, ct: ct).ConfigureAwait(false);
            report = Analyze(address, logs.Events);
            report.LogRead = logs.Streams;
            report.Warnings = logs.Warnings;
            report.Bounded = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            report.ReadError = exception.Message;
        }

        report.SessionId = sessionId;
        report.ProcessStatus = session.Status;
        report.GameActive = gameActive;
        return report;
    }

    /// <summary>Only attributed client logger events following a matching connection attempt change the observation.</summary>
    public static ServerConnectionReport Analyze(string requestedServer, IReadOnlyList<LogEvent> events)
    {
        var requested = ServerService.ParseAddress(requestedServer);
        var result = new ServerConnectionReport { Server = requestedServer };
        var matched = false;
        for (var eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            var entry = events[eventIndex];
            if (entry.Stream != "stdoutLog") continue;
            var logger = DiagnosticLogFields.Logger(entry);
            var message = entry.Message;
            if (logger is "net.minecraft.client.gui.screens.ConnectScreen" or "net.minecraft.class_412"
                && message.StartsWith("Connecting to ", StringComparison.Ordinal))
            {
                var endpoint = message["Connecting to ".Length..];
                var separator = endpoint.LastIndexOf(", ", StringComparison.Ordinal);
                matched = separator > 0 && int.TryParse(endpoint[(separator + 2)..], out var port)
                    && requested.Port == port && string.Equals(requested.Host.Trim('[', ']'), endpoint[..separator].Trim('[', ']'), StringComparison.OrdinalIgnoreCase);
                result.Observation = matched ? "attempt_logged" : "different_target";
                result.AttemptEventIndex = eventIndex;
                result.IssueCode = "unknown";
                result.Evidence = matched ? Bounded(message) : null;
                result.Source = matched ? "client-log" : "none";
                result.Logger = null;
                result.Timestamp = null;
                result.RegistryEntries = [];
                continue;
            }

            if (!matched) continue;
            if (logger == "FabricRegistrySync" && (message.StartsWith("Received unknown remote registry entries", StringComparison.Ordinal)
                || message.StartsWith("Registry entry (", StringComparison.Ordinal)))
            {
                result.Observation = "connect_failed";
                result.IssueCode = "registry_mismatch";
                result.Source = "client-log";
                result.Logger = logger;
                result.Timestamp = DiagnosticLogFields.Timestamp(entry);
                result.Evidence = Bounded((result.Evidence ?? string.Empty) + Environment.NewLine + message);
                var identifiers = result.RegistryEntries;
                if (identifiers is not null && identifiers.Count < 64)
                {
                    var start = message.IndexOf('(');
                    var end = start < 0 ? -1 : message.IndexOf(')', start + 1);
                    if (start >= 0 && end > start)
                    {
                        var identifier = message[(start + 1)..end];
                        if (!identifiers.Contains(identifier, StringComparer.Ordinal)) identifiers.Add(identifier);
                    }
                }

                continue;
            }

            var failed = logger == "net.minecraft.client.gui.screens.ConnectScreen" && message == "Couldn't connect to server";
            var closed = logger == "net.minecraft.client.multiplayer.ClientCommonPacketListenerImpl"
                && message.StartsWith("Client disconnected with reason: ", StringComparison.Ordinal);
            if (!failed && !closed) continue;
            var evidence = message + (DiagnosticLogFields.Exception(entry) is { Length: > 0 } exception ? Environment.NewLine + exception : string.Empty);
            result.Observation = failed ? "connect_failed" : "connection_closed";
            var issue = Classify(evidence);
            result.IssueCode = issue == "unknown" && result.IssueCode == "registry_mismatch" ? "registry_mismatch" : issue;
            result.Source = "client-log";
            result.Evidence = Bounded(evidence);
            result.Logger = logger;
            result.Timestamp = DiagnosticLogFields.Timestamp(entry);
        }

        return result;
    }

    public static string Classify(string evidence)
    {
        if (evidence.Contains("java.net.UnknownHostException", StringComparison.Ordinal)) return "dns_failed";
        if (evidence.Contains("Connection refused", StringComparison.Ordinal)) return "connection_refused";
        if (evidence.Contains("io.netty.channel.ConnectTimeoutException", StringComparison.Ordinal)
            || evidence.Contains("java.net.SocketTimeoutException", StringComparison.Ordinal))
        {
            return "server_timeout";
        }

        if (evidence.Contains("java.net.NoRouteToHostException", StringComparison.Ordinal)) return "network_unreachable";
        if (evidence.Contains("You are not white-listed on this server", StringComparison.Ordinal)) return "whitelist";
        if (evidence.Contains("You are banned from this server", StringComparison.Ordinal)) return "banned";
        if (evidence.Contains("The server is full!", StringComparison.Ordinal)) return "server_full";
        if (evidence.Contains("Invalid session", StringComparison.Ordinal)
            || evidence.Contains("Failed to verify username!", StringComparison.Ordinal))
        {
            return "authentication";
        }

        if (evidence.Contains("Outdated client!", StringComparison.Ordinal)
            || evidence.Contains("Outdated server!", StringComparison.Ordinal))
        {
            return "version_mismatch";
        }

        return "unknown";
    }

    private static string Bounded(string value) => value.Length <= 4096 ? value : value[..4096];
}