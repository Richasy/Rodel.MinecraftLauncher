// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>The fixed current-user RPC envelope; data is serialized only at this heterogeneous method boundary.</summary>
internal sealed record AiHostFrame
{
    /// <summary>Gets the negotiated protocol version.</summary>
    public int? Version { get; init; }
    /// <summary>Gets the exact request identity used for correlation and cancellation.</summary>
    public string? RequestId { get; init; }
    /// <summary>Gets the requested method or result/cancel control message.</summary>
    public string? Method { get; init; }
    /// <summary>Gets the canonical data-root identity authenticated by the handshake.</summary>
    public string? RootIdentity { get; init; }
    /// <summary>Gets the Windows user identity bound to this pipe.</summary>
    public string? UserSid { get; init; }
    /// <summary>Gets the serialized method-specific request, result or open diagnostic details at the RPC boundary.</summary>
    public JsonNode? Data { get; init; }
    /// <summary>Gets the response outcome; absent on request and control frames.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }
    /// <summary>Gets the fixed Core operation error; absent on successful frames.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LauncherOperationError? Error { get; init; }
    /// <summary>Gets future envelope fields without discarding their original JSON values.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AiHostFrame))]
internal sealed partial class AiHostJsonContext : JsonSerializerContext;

internal sealed class AiHostProtocol
{
    public const int Version = 1;
    public const int MaximumFrameBytes = FramedJsonPipe.MaximumFrameBytes;

    public AiHostProtocol(LauncherPaths paths)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The AI host uses current-user Windows named pipes.");
        UserSid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("The current Windows user has no SID.");
        CanonicalRoot = Path.TrimEndingDirectorySeparator(ResourceLease.CanonicalPath(paths.Root));
        RootIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalRoot.ToUpperInvariant())));
        PipeName = "rodel-assistant-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(UserSid + ":" + RootIdentity)));
    }

    public string CanonicalRoot { get; }
    public string RootIdentity { get; }
    public string UserSid { get; }
    public string PipeName { get; }

    /// <summary>Creates an identity-bound frame without serializing its payload again.</summary>
    public AiHostFrame Envelope(string requestId, string method, JsonNode? data = null) => new()
    {
        Version = Version,
        RequestId = requestId,
        Method = method,
        RootIdentity = RootIdentity,
        UserSid = UserSid,
        Data = data?.DeepClone()
    };

    /// <summary>Rejects protocol, root, user and request-identity mismatches before dispatch or accepting a receipt.</summary>
    public void Validate(AiHostFrame frame, string? requestId = null)
    {
        if (frame.Version != Version
            || frame.RootIdentity != RootIdentity
            || frame.UserSid != UserSid
            || !Guid.TryParseExact(frame.RequestId, "N", out _)
            || (requestId is not null && frame.RequestId != requestId))
        {
            throw new LauncherOperationException("AI_HOST_PROTOCOL_MISMATCH", "The AI host protocol, user, data root or request identity does not match. No request was replayed.");
        }
    }

    /// <summary>Reads the shared bounded frame with AI-runtime error identifiers.</summary>
    public static async Task<AiHostFrame> ReadAsync(Stream stream, CancellationToken ct)
        => (await FramedJsonPipe.ReadAsync(stream, "AI_HOST", ct).ConfigureAwait(false)).Deserialize(AiHostJsonContext.Default.AiHostFrame)
            ?? throw new LauncherOperationException("AI_HOST_FRAME_INVALID", "Expected a host JSON object.");
    /// <summary>Serializes the fixed envelope only when writing the shared bounded pipe frame.</summary>
    public static Task WriteAsync(Stream stream, AiHostFrame frame, CancellationToken ct)
        => FramedJsonPipe.WriteAsync(stream, JsonSerializer.SerializeToNode(frame, AiHostJsonContext.Default.AiHostFrame)!.AsObject(), "AI_HOST", ct);
}