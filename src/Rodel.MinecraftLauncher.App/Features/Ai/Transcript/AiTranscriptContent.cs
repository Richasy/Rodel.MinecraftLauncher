// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

internal enum AiTranscriptPartKind
{
    Thinking,
    Response,
    Tool,
    Notice,
    Question
}

/// <summary>Contains only player-facing content, never a durable event envelope.</summary>
internal sealed class AiTranscriptPart(AiTranscriptPartKind kind, string id)
{
    internal AiTranscriptPartKind Kind { get; } = kind;
    internal string Id { get; } = id;
    internal string Text { get; set; } = string.Empty;
    internal string Name { get; set; } = string.Empty;
    internal string Arguments { get; set; } = string.Empty;
    internal string Result { get; set; } = string.Empty;
    internal string Status { get; set; } = string.Empty;
    internal string Error { get; set; } = string.Empty;
    internal string PromptId { get; set; } = string.Empty;
    internal string Answer { get; set; } = string.Empty;
    internal string AnswerStatus { get; set; } = string.Empty;
    internal bool Complete { get; set; }
    internal DateTimeOffset? StartedUtc { get; set; }
    internal DateTimeOffset? FinishedUtc { get; set; }
    internal TimeSpan? Elapsed => Kind != AiTranscriptPartKind.Question && StartedUtc is { } started && (FinishedUtc ?? (Complete ? null : DateTimeOffset.UtcNow)) is { } end && end >= started ? end - started : null;
}

/// <summary>Projects an explicit event allowlist into ordered model content and tool exchanges.</summary>
internal static class AiTranscriptContent
{
    /// <summary>Includes bounded provider diagnostics in the explicitly opened failure details without changing telemetry messages.</summary>
    internal static string ErrorText(JsonNode? error)
    {
        if (error is not JsonObject record)
            return error?.ToString() ?? string.Empty;
        var message = record["message"]?.ToString() ?? string.Empty;
        var details = record["details"] as JsonObject;
        var reason = details?["inferenceDetail"]?.ToString() ?? details?["providerMessage"]?.ToString();
        return string.IsNullOrWhiteSpace(reason) || message.Contains(reason, StringComparison.Ordinal) ? message : message + Environment.NewLine + reason;
    }

    internal static AiTranscriptPart[] Read(IEnumerable<AiChatEvent> events, string fallbackAnswer, bool terminal)
    {
        var parts = new List<AiTranscriptPart>();
        var byStep = new Dictionary<string, AiTranscriptPart>(StringComparer.Ordinal);
        var notices = new HashSet<string>(StringComparer.Ordinal);
        AiTranscriptPart Part(AiChatEvent item, AiTranscriptPartKind kind, string? promptId = null)
        {
            var step = promptId ?? (item.AdditionalData?.TryGetValue("stepId", out var value) == true && value.ValueKind == JsonValueKind.String ? value.GetString() : null);
            step ??= item.Data?["parentStepId"]?.ToString() + ":" + item.Data?["toolCallId"]?.ToString();
            // User interaction and tool lifecycle events share the host invocation, never the provider's reusable call ID.
            var id = (kind == AiTranscriptPartKind.Question ? AiTranscriptPartKind.Tool : kind) + ":" + step;
            if (byStep.TryGetValue(id, out var existing))
            {
                if (item.Kind is "reasoning_finished" or "message_finished" or "tool_finished")
                    existing.FinishedUtc = Time(item);
                return existing;
            }

            var created = new AiTranscriptPart(kind, id)
            {
                StartedUtc = Time(item),
                PromptId = kind == AiTranscriptPartKind.Question ? step : string.Empty
            };
            parts.Add(created);
            byStep.Add(id, created);
            return created;
        }

        foreach (var item in events)
        {
            switch (item.Kind)
            {
                case "reasoning_started":
                case "reasoning_progress":
                    _ = Part(item, AiTranscriptPartKind.Thinking);
                    break;
                case "reasoning_delta":
                    Part(item, AiTranscriptPartKind.Thinking).Text += item.Data?["text"]?.ToString();
                    break;
                case "reasoning_finished":
                    Part(item, AiTranscriptPartKind.Thinking).Complete = true;
                    break;
                case "message_started":
                    _ = Part(item, AiTranscriptPartKind.Response);
                    break;
                case "text_delta":
                    Part(item, AiTranscriptPartKind.Response).Text += item.Data?["text"]?.ToString();
                    break;
                case "message_finished":
                    Part(item, AiTranscriptPartKind.Response).Complete = true;
                    break;
                case "tool_started":
                case "tool_finished":
                    var tool = Part(item, item.Data?["name"]?.ToString() == "ask_user" ? AiTranscriptPartKind.Question : AiTranscriptPartKind.Tool);
                    tool.Name = item.Data?["name"]?.ToString() ?? tool.Name;
                    if (item.Kind == "tool_started")
                    {
                        tool.Arguments = item.Data?["arguments"]?.ToJsonString() ?? string.Empty;
                        if (tool.Kind == AiTranscriptPartKind.Question)
                            tool.Text = item.Data?["arguments"]?["question"]?.ToString() ?? string.Empty;
                    }
                    else
                    {
                        tool.Result = item.Data?["result"]?.ToJsonString() ?? string.Empty;
                        tool.Status = item.Data?["status"]?.ToString() ?? string.Empty;
                        tool.Error = item.Data?["result"] is JsonObject result && result["error"] is JsonObject error ? error["message"]?.ToString() ?? string.Empty : string.Empty;
                        tool.Complete = true;
                        if (tool.Kind == AiTranscriptPartKind.Question && item.Data?["result"]?["data"] is JsonObject receipt)
                            ReadAnswer(tool, receipt);
                    }

                    break;
                case "user_prompt_required":
                    var question = Part(item, AiTranscriptPartKind.Question, item.Data?["id"]?.ToString());
                    question.Text = item.Data?["question"]?["question"]?.ToString() ?? question.Text;
                    break;
                case "user_prompt_answered":
                    ReadAnswer(Part(item, AiTranscriptPartKind.Question, item.Data?["promptId"]?.ToString()), item.Data);
                    break;
                case "turn_finished":
                    foreach (var open in parts.Where(part => !part.Complete))
                        open.FinishedUtc ??= Time(item);
                    break;
                case "tool_catalog_error":
                case "managed_mcp_unavailable":
                case "managed_mcp_cleanup_warning":
                case "mcp_cleanup_error":
                case "context_compaction_warning":
                    var message = item.Data?["error"]?["message"]?.ToString() ?? item.Data?["message"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(message) && notices.Add(message))
                        parts.Add(new(AiTranscriptPartKind.Notice, "notice:" + item.Sequence) { Text = message });
                    break;
            }
        }

        if (!parts.Any(part => part.Kind == AiTranscriptPartKind.Response && part.Text.Length > 0) && fallbackAnswer.Length > 0)
        {
            parts.Add(new(AiTranscriptPartKind.Response, "answer")
            {
                Text = fallbackAnswer,
                Complete = terminal
            });
        }

        if (terminal)
        {
            foreach (var part in parts)
                part.Complete = true;
        }

        return [.. parts.Where(part => part.Kind is not (AiTranscriptPartKind.Response or AiTranscriptPartKind.Thinking) || !string.IsNullOrWhiteSpace(part.Text))];
    }

    private static void ReadAnswer(AiTranscriptPart part, JsonObject? response)
    {
        if (response?["status"]?.ToString() is not ("answered" or "skipped"))
            return;
        var labels = (response["choices"] as JsonArray ?? []).Select(choice => choice?["label"]?.ToString());
        part.Text = response["question"]?.ToString() ?? part.Text;
        part.Answer = string.Join(" · ", labels.Append(response["text"]?.ToString()).Where(text => !string.IsNullOrWhiteSpace(text)));
        part.AnswerStatus = response["status"]!.ToString();
        part.Complete = true;
    }

    private static DateTimeOffset? Time(AiChatEvent item) => item.AdditionalData?.TryGetValue("timeUtc", out var value) == true && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time : null;
}