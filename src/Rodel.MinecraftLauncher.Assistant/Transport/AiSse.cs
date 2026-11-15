// Copyright (c) Richasy. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant;

internal static class AiSse
{
    // Follows the SSE framing used by Agent.Providers.Shared, with explicit line/event/stream bounds.
    /// <summary>Reads bounded JSON SSE frames and rejects truncated frames without owning the input stream.</summary>
    public static async IAsyncEnumerable<JsonObject> ReadAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        var line = new MemoryStream();
        var data = new StringBuilder();
        var buffer = new byte[8192];
        long total = 0;
        while (true)
        {
            var count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (count == 0)
                break;
            total += count;
            if (total > 16 * 1024 * 1024)
                throw new AiProtocolException("AI_RESPONSE_TOO_LARGE", "The model stream exceeded its byte budget.");
            for (var index = 0; index < count; index++)
            {
                if (buffer[index] != (byte)'\n')
                {
                    if (line.Length >= 1024 * 1024)
                        throw new AiProtocolException("AI_RESPONSE_TOO_LARGE", "An SSE line exceeded its byte budget.");
                    line.WriteByte(buffer[index]);
                    continue;
                }

                var value = Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
                line.SetLength(0);
                if (value.Length == 0)
                {
                    if (data.Length == 0)
                        continue;
                    var payload = data.ToString().TrimEnd('\n');
                    data.Clear();
                    if (payload == "[DONE]")
                    {
                        yield return new JsonObject { ["type"] = "$done" };
                        yield break;
                    }

                    JsonObject item;
                    try
                    {
                        item = JsonNode.Parse(payload) as JsonObject
                            ?? throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "Expected a JSON object in the SSE data event.");
                    }
                    catch (JsonException)
                    {
                        throw new AiProtocolException("AI_PROVIDER_PROTOCOL_ERROR", "The provider returned non-JSON SSE data. Partial content is retained; this response was not replayed.");
                    }

                    if (item["error"] is not null || item["type"]?.ToString() == "error")
                        throw new AiProtocolException("AI_PROVIDER_ERROR", item["error"]?["message"]?.ToString() ?? "The provider reported a streaming error.");
                    yield return item;
                }
                else if (value.StartsWith("data:", StringComparison.Ordinal))
                {
                    var fragment = value[5..];
                    if (fragment.StartsWith(' '))
                        fragment = fragment[1..];
                    data.Append(fragment).Append('\n');
                    if (data.Length > 1024 * 1024)
                        throw new AiProtocolException("AI_RESPONSE_TOO_LARGE", "An SSE event exceeded its character budget.");
                }
            }
        }

        if (line.Length != 0 || data.Length != 0)
            throw new AiProtocolException("AI_RESPONSE_INTERRUPTED", "The event stream ended inside a frame. No incomplete tool input was executed.");
    }
}