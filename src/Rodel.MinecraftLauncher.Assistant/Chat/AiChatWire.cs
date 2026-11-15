// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant;

internal static class AiChatWire
{
    /// <summary>Maps conversation history and offered tools to the selected HTTP provider's streaming request.</summary>
    public static AiProviderRequest Build(AiProviderContext snapshot, IReadOnlyList<ChatMessage> history, IReadOnlyList<AIFunctionDeclaration> tools, int? maximumOutput)
    {
        var protocol = snapshot.Protocol;
        var model = snapshot;
        var outputLimit = maximumOutput ?? model.MaxOutputTokens;
        var messages = new JsonArray();
        var pendingImages = new JsonArray();
        void FlushToolImages()
        {
            if (pendingImages.Count == 0) return;
            messages.Add((JsonNode)new JsonObject { ["role"] = "user", ["content"] = pendingImages });
            pendingImages = [];
        }

        var system = string.Join("\n\n", history.Where(item => item.Role == ChatRole.System).Select(item => item.Text));
        foreach (var item in history)
        {
            var role = item.Role.Value;
            if (role != "tool") FlushToolImages();
            var result = item.Contents.OfType<FunctionResultContent>().SingleOrDefault();
            var text = result is null ? item.Text : AiChatMessages.ResultText(result);
            var state = AiChatMessages.Native(item);
            var native = AiChatMessages.Matches(state, snapshot) ? state!.Payload : null;
            var calls = item.Contents.OfType<FunctionCallContent>().ToArray();
            if (protocol is AiProviderProtocols.OpenAiChat)
            {
                if (role == "tool")
                {
                    messages.Add((JsonNode)new JsonObject { ["role"] = "tool", ["tool_call_id"] = result!.CallId, ["content"] = text });
                }
                else if (native is JsonObject original)
                {
                    messages.Add(original.DeepClone());
                }
                else
                {
                    var message = new JsonObject
                    {
                        ["role"] = role,
                        ["content"] = item.Contents.OfType<DataContent>().Any()
                            ? ImageBlocks(item, "text", "image_url") : JsonValue.Create(text)
                    };
                    if (calls.Length > 0)
                    {
                        message["tool_calls"] = new JsonArray(calls.Select(call => (JsonNode)new JsonObject
                        {
                            ["id"] = call.CallId,
                            ["type"] = "function",
                            ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = AiChatMessages.Arguments(call).ToJsonString() }
                        }).ToArray());
                    }

                    messages.Add((JsonNode)message);
                }
            }
            else if (protocol == AiProviderProtocols.OpenAiResponses)
            {
                if (native is JsonArray outputs)
                {
                    foreach (var output in outputs) messages.Add(output!.DeepClone());
                }
                else if (role == "tool")
                {
                    messages.Add((JsonNode)new JsonObject { ["type"] = "function_call_output", ["call_id"] = result!.CallId, ["output"] = text });
                }
                else
                {
                    if (item.Contents.OfType<DataContent>().Any())
                        messages.Add((JsonNode)new JsonObject { ["role"] = role, ["content"] = ImageBlocks(item, "input_text", "input_image") });
                    else if (text.Length > 0)
                        messages.Add((JsonNode)new JsonObject { ["role"] = role, ["content"] = text });
                    foreach (var call in calls)
                        messages.Add((JsonNode)new JsonObject { ["type"] = "function_call", ["call_id"] = call.CallId, ["name"] = call.Name, ["arguments"] = AiChatMessages.Arguments(call).ToJsonString() });
                }
            }
            else if (protocol == AiProviderProtocols.AnthropicMessages)
            {
                if (role == "system")
                    continue;
                JsonArray content;
                if (native is JsonArray blocks)
                {
                    content = blocks.DeepClone().AsArray();
                }
                else if (role == "tool")
                {
                    JsonNode body = JsonValue.Create(text)!;
                    if (item.Contents.OfType<DataContent>().Any())
                    {
                        var outputBlocks = new JsonArray((JsonNode)new JsonObject { ["type"] = "text", ["text"] = text });
                        foreach (var image in item.Contents.OfType<DataContent>()) outputBlocks.Add((JsonNode)AnthropicImage(image));
                        body = outputBlocks;
                    }

                    content = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = result!.CallId, ["content"] = body });
                }
                else
                {
                    content = [];
                    if (text.Length > 0) content.Add((JsonNode)new JsonObject { ["type"] = "text", ["text"] = text });
                    foreach (var image in item.Contents.OfType<DataContent>())
                    {
                        content.Add((JsonNode)AnthropicImage(image));
                    }

                    foreach (var call in calls)
                        content.Add((JsonNode)new JsonObject { ["type"] = "tool_use", ["id"] = call.CallId, ["name"] = call.Name, ["input"] = AiChatMessages.Arguments(call) });
                }

                var wireRole = role == "tool" ? "user" : role;
                if (messages.LastOrDefault()?["role"]?.ToString() == wireRole)
                    foreach (var block in content) messages.Last()!["content"]!.AsArray().Add(block!.DeepClone());
                else
                    messages.Add((JsonNode)new JsonObject { ["role"] = wireRole, ["content"] = content });
            }
            else
            {
                if (role == "system")
                    continue;
                JsonArray parts;
                if (native is JsonArray originals)
                {
                    parts = originals.DeepClone().AsArray();
                }
                else if (role == "tool")
                {
                    parts = new JsonArray(new JsonObject { ["functionResponse"] = new JsonObject { ["name"] = AiChatMessages.ToolName(result!), ["response"] = new JsonObject { ["result"] = JsonNode.Parse(text) } } });
                    if (AiChatMessages.Matches(state, snapshot) && AiChatMessages.ProviderCallId(result!) is { } providerId)
                    {
                        parts[0]!["functionResponse"]!["id"] = providerId;
                    }

                    foreach (var image in item.Contents.OfType<DataContent>())
                        parts.Add((JsonNode)GeminiImage(image));
                }
                else
                {
                    parts = [];
                    if (text.Length > 0) parts.Add((JsonNode)new JsonObject { ["text"] = text });
                    foreach (var image in item.Contents.OfType<DataContent>())
                        parts.Add((JsonNode)GeminiImage(image));
                    foreach (var call in calls)
                        parts.Add((JsonNode)new JsonObject { ["functionCall"] = new JsonObject { ["name"] = call.Name, ["args"] = AiChatMessages.Arguments(call) } });
                }

                if (role == "tool" && messages.LastOrDefault()?["role"]?.ToString() == "user"
                    && messages.Last()!["parts"] is JsonArray existing && existing.Any(part => part?["functionResponse"] is not null)
                    && existing.All(part => part?["functionResponse"] is not null || part?["inlineData"] is not null))
                {
                    foreach (var part in parts) existing.Add(part!.DeepClone());
                }
                else
                {
                    messages.Add((JsonNode)new JsonObject { ["role"] = role == "assistant" ? "model" : "user", ["parts"] = parts });
                }
            }

            if (role == "tool" && protocol is AiProviderProtocols.OpenAiChat or AiProviderProtocols.OpenAiResponses
                && item.Contents.OfType<DataContent>().Any())
            {
                var responses = protocol == AiProviderProtocols.OpenAiResponses;
                pendingImages.Add((JsonNode)new JsonObject
                {
                    ["type"] = responses ? "input_text" : "text",
                    ["text"] = AiChatMessages.ToolImageLabel(result!)
                });
                foreach (var block in ImageBlocks(item, responses ? "input_text" : "text", responses ? "input_image" : "image_url"))
                    pendingImages.Add(block!.DeepClone());
            }
        }

        FlushToolImages();

        if (protocol == AiProviderProtocols.OpenAiResponses)
        {
            return new AiOpenAiResponsesRequest
            {
                Model = model.Model,
                Stream = true,
                Input = messages,
                Include = ["reasoning.encrypted_content"],
                MaxOutputTokens = outputLimit,
                Temperature = model.Temperature,
                Tools = tools.Count == 0 ? null : [.. tools.Select(tool =>
                    new AiOpenAiResponsesRequest.Tool(tool.Name, tool.Description ?? string.Empty, AiToolDeclarations.Schema(tool)))]
            };
        }
        else if (protocol == AiProviderProtocols.OpenAiChat)
        {
            return new AiOpenAiChatRequest
            {
                Model = model.Model,
                Stream = true,
                Messages = messages,
                MaxCompletionTokens = outputLimit,
                StreamOptions = new(true),
                Temperature = model.Temperature,
                Tools = tools.Count == 0 ? null : [.. tools.Select(tool => new AiOpenAiChatRequest.Tool(
                    new(tool.Name, tool.Description ?? string.Empty, AiToolDeclarations.Schema(tool))))]
            };
        }
        else if (protocol == AiProviderProtocols.AnthropicMessages)
        {
            return new AiAnthropicRequest
            {
                Model = model.Model,
                Stream = true,
                Messages = messages,
                System = system,
                MaxTokens = outputLimit,
                Temperature = model.Temperature,
                Tools = tools.Count == 0 ? null : [.. tools.Select(tool =>
                    new AiAnthropicRequest.Tool(tool.Name, tool.Description ?? string.Empty, AiToolDeclarations.Schema(tool)))]
            };
        }
        else
        {
            return new AiGeminiRequest
            {
                Contents = messages,
                SystemInstruction = new([new(system)]),
                GenerationConfig = new() { MaxOutputTokens = outputLimit, CandidateCount = 1, Temperature = model.Temperature },
                Tools = tools.Count == 0 ? null : [new([.. tools.Select(tool =>
                    new AiGeminiRequest.FunctionDefinition(tool.Name, tool.Description ?? string.Empty, AiToolDeclarations.Schema(tool)))])]
            };
        }

    }

    private static JsonArray ImageBlocks(ChatMessage message, string textType, string imageType)
    {
        var blocks = new JsonArray();
        foreach (var content in message.Contents)
        {
            if (content is TextContent { Text: { Length: > 0 } text })
            {
                blocks.Add((JsonNode)new JsonObject { ["type"] = textType, ["text"] = text });
            }
            else if (content is DataContent image && image.HasTopLevelMediaType("image"))
            {
                var uri = image.Uri.ToString();
                blocks.Add((JsonNode)(imageType == "input_image"
                    ? new JsonObject { ["type"] = imageType, ["image_url"] = uri }
                    : new JsonObject { ["type"] = imageType, ["image_url"] = new JsonObject { ["url"] = uri } }));
            }
        }

        return blocks;
    }

    private static JsonObject AnthropicImage(DataContent image) => new()
    {
        ["type"] = "image",
        ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = image.MediaType, ["data"] = Convert.ToBase64String(image.Data.Span) }
    };

    private static JsonObject GeminiImage(DataContent image) => new()
    {
        ["inlineData"] = new JsonObject { ["mimeType"] = image.MediaType, ["data"] = Convert.ToBase64String(image.Data.Span) }
    };
}