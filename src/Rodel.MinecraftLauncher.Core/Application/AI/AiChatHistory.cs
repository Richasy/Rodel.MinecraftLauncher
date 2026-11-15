// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Assistant;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Separates complete MEAI message persistence from Core's portable archive and effect projections.</summary>
internal static class AiChatHistory
{
    /// <summary>Reads complete model messages or Core-authored user/checkpoint/tool records without rewriting archived evidence.</summary>
    public static ChatMessage[] Read(JsonArray history) => [.. history.Select(ReadMessage)];

    private static ChatMessage ReadMessage(JsonNode? item)
    {
        if (item is not JsonObject record) throw new JsonException("Conversation history must contain message objects.");
        if (record["meai"] is { } complete)
        {
            if (JsonSerializer.Deserialize(complete.ToJsonString(),
                AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(ChatMessage))) is not ChatMessage completeMessage
                || string.IsNullOrEmpty(completeMessage.Role.Value))
            {
                throw new JsonException("The complete model message is invalid.");
            }

            return completeMessage;
        }

        var role = record["role"]?.GetValue<string>() ?? throw new JsonException("A history message has no role.");
        var text = record["text"]?.GetValue<string>() ?? string.Empty;
        var message = new ChatMessage(new ChatRole(role), text);
        if (role == "tool")
        {
            var result = new FunctionResultContent(record["callId"]!.GetValue<string>(), text);
            AiChatMessages.SetToolName(result, record["name"]?.GetValue<string>());
            AiChatMessages.SetProviderCallId(result, record["providerCallId"]?.GetValue<string>());
            message.Contents = [result];
            if (record["nativeProtocol"]?.GetValue<string>() is { } protocol
                && record["nativeModelId"]?.GetValue<string>() is { } model
                && record["nativeServiceRevision"]?.GetValue<long>() is { } revision)
            {
                AiChatMessages.Bind(message, new(protocol, model, revision, new JsonObject()));
            }
        }
        else if (record["calls"] is JsonArray calls)
        {
            foreach (var call in calls)
            {
                message.Contents.Add(AiChatMessages.Call(call!["id"]?.GetValue<string>(), call["name"]?.GetValue<string>(),
                    call["arguments"]?.DeepClone() ?? throw new JsonException("A stored tool call has no complete arguments.")));
            }
        }

        return message;
    }

    /// <summary>Replaces archived tool text while retaining inline media and the original provider correlation binding.</summary>
    public static JsonObject WithToolText(JsonObject record, string text)
    {
        var original = ReadMessage(record);
        var tool = original.Contents.OfType<FunctionResultContent>().SingleOrDefault();
        IList<AIContent> contents;
        if (tool is not null)
        {
            var changed = new FunctionResultContent(tool.CallId, text)
            {
                AdditionalProperties = tool.AdditionalProperties is null ? null : new(tool.AdditionalProperties)
            };
            contents = [changed, .. original.Contents.OfType<DataContent>()];
        }
        else
        {
            contents = [new TextContent(text), .. original.Contents.OfType<DataContent>()];
        }

        var message = new ChatMessage(original.Role, contents);
        if (tool is not null && AiChatMessages.Native(original) is { } state)
            AiChatMessages.Bind(message, state with { Payload = new JsonObject() });
        var changedRecord = record.DeepClone().AsObject();
        changedRecord["text"] = text;
        changedRecord["meai"] = Write(message)["meai"]!.DeepClone();
        return changedRecord;
    }

    /// <summary>Atomically includes the full standard message and its non-authoritative portable archive projection.</summary>
    public static JsonObject Write(ChatMessage message)
    {
        var calls = new JsonArray();
        foreach (var call in message.Contents.OfType<FunctionCallContent>())
        {
            var value = new JsonObject
            {
                ["id"] = call.CallId,
                ["name"] = call.Name,
                ["arguments"] = AiChatMessages.Arguments(call)
            };
            if (AiChatMessages.ProviderCallId(call) is { } providerId) value["providerCallId"] = providerId;
            calls.Add((JsonNode)value);
        }

        var tool = message.Contents.OfType<FunctionResultContent>().SingleOrDefault();
        var record = new JsonObject
        {
            ["role"] = message.Role.Value,
            ["text"] = tool is null ? message.Text : AiChatMessages.ResultText(tool),
            ["reasoning"] = string.Concat(message.Contents.OfType<TextReasoningContent>().Select(content => content.Text)),
            ["calls"] = calls,
            ["meai"] = JsonSerializer.SerializeToNode(message, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(ChatMessage)))
        };
        if (tool is not null)
        {
            record["callId"] = tool.CallId;
            record["name"] = AiChatMessages.ToolName(tool);
            record["providerCallId"] = AiChatMessages.ProviderCallId(tool);
            if (AiChatMessages.Native(message) is { } state)
            {
                record["nativeProtocol"] = state.Protocol;
                record["nativeModelId"] = state.ModelId;
                record["nativeServiceRevision"] = state.ServiceRevision;
            }
        }

        return record;
    }
}