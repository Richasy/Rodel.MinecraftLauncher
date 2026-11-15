// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>Preserves the launcher's public accounting field names without introducing a parallel runtime usage model.</summary>
internal sealed class AiUsageDetailsConverter : JsonConverter<UsageDetails>
{
    /// <inheritdoc />
    public override UsageDetails Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;
        static long? Count(JsonElement item, string name) => item.TryGetProperty(name, out var count) && count.ValueKind != JsonValueKind.Null
            ? count.GetInt64() : null;
        return new()
        {
            InputTokenCount = Count(value, "inputTokens"),
            OutputTokenCount = Count(value, "outputTokens"),
            TotalTokenCount = Count(value, "totalTokens"),
            ReasoningTokenCount = Count(value, "reasoningTokens")
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, UsageDetails value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        WriteCount(writer, "inputTokens", value.InputTokenCount);
        WriteCount(writer, "outputTokens", value.OutputTokenCount);
        WriteCount(writer, "totalTokens", value.TotalTokenCount);
        if (value.ReasoningTokenCount is { } reasoning) writer.WriteNumber("reasoningTokens", reasoning);
        writer.WriteEndObject();
    }

    private static void WriteCount(Utf8JsonWriter writer, string name, long? count)
    {
        if (count is { } value) writer.WriteNumber(name, value);
        else writer.WriteNull(name);
    }
}