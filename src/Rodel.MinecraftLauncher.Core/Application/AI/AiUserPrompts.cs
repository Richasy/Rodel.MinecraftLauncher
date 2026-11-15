// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>A stable choice offered by the model; selecting it does not authorize an operation.</summary>
/// <param name="Id">The stable identifier submitted by the user interface.</param>
/// <param name="Label">The player-visible choice.</param>
/// <param name="Description">Optional detail explaining the choice.</param>
public sealed record AiUserOption(
    [property: System.ComponentModel.Description("Stable option identifier, unique within this question.")] string Id,
    string Label,
    string? Description = null);

/// <summary>One focused question, with optional choices and an always available free-text response.</summary>
/// <param name="Question">The focused question shown to the player.</param>
/// <param name="Options">Available choices, or an empty array for a text-only question.</param>
/// <param name="Multiple">Whether multiple choices can be selected together.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiUserQuestion(string Question, AiUserOption[] Options, bool Multiple = false);

/// <summary>The exact durable question currently blocking a model tool call.</summary>
/// <param name="Id">Unique invocation identifier binding the question to its turn.</param>
/// <param name="Question">The frozen question and choices.</param>
public sealed record AiUserPrompt(string Id, AiUserQuestion Question);

/// <summary>Explicitly submits or skips one question in one conversation turn.</summary>
public sealed record AiUserResponse
{
    /// <summary>Initializes optional answers to empty values when a response JSON omits them.</summary>
    public AiUserResponse() { }
    /// <summary>Reads a response with optional choices/text while preserving their defaults when omitted in JSON.</summary>
    /// <param name="conversationId">The conversation containing the question.</param>
    /// <param name="turnId">The waiting turn.</param>
    /// <param name="promptId">The exact question invocation.</param>
    /// <param name="optionIds">Explicit selected choices, or none for free text or skip.</param>
    /// <param name="text">The user's answer or clarification.</param>
    /// <param name="skip">Whether the user explicitly skips the question.</param>
    [JsonConstructor]
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public AiUserResponse(string conversationId, string turnId, string promptId, string[]? optionIds = null, string text = "", bool skip = false)
    {
        ConversationId = conversationId; TurnId = turnId; PromptId = promptId;
        OptionIds = optionIds ?? []; Text = text; Skip = skip;
    }
    /// <summary>Gets the conversation containing the waiting turn.</summary>
    public required string ConversationId { get; init; }
    /// <summary>Gets the waiting turn identifier.</summary>
    public required string TurnId { get; init; }
    /// <summary>Gets the immutable question identifier returned by the turn.</summary>
    public required string PromptId { get; init; }
    /// <summary>Gets selected option identifiers; labels are resolved from the frozen question.</summary>
    public string[] OptionIds { get; init; } = [];
    /// <summary>Gets the user's own answer or clarification.</summary>
    public string Text { get; init; } = string.Empty;
    /// <summary>Gets whether the user explicitly declines to answer, rather than submitting an empty answer.</summary>
    public bool Skip { get; init; }
}

/// <summary>Provides generated metadata for model questions, durable prompts and explicit user responses.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(AiUserQuestion))]
[JsonSerializable(typeof(AiUserPrompt))]
[JsonSerializable(typeof(AiUserResponse))]
public sealed partial class AiUserPromptJsonContext : JsonSerializerContext;

internal sealed class AiUserPrompts(LauncherPaths paths)
{
    /// <summary>Direct system-level interaction policy for ordinary AI chat with tools enabled; skin editing is excluded.</summary>
    internal const string SystemInstructions = """
        用户交互规则：
        - 当继续任务确实需要用户提供缺失信息、表达偏好或作出选择时，必须主动调用 ask_user，并等待工具返回答案；不要等用户提醒你调用工具。
        - 不要用最终回复中的问题清单、“我需要你回答…”、“请回复选项…”或“确认后我再继续”代替 ask_user。可以简短交代提问原因，但实际需要用户回应的问题必须由工具发起。
        - 先用本轮目录中已提供、已授权的只读工具查明可查询的事实，不让用户重复提供已有信息，也不为这类查询额外询问许可。无法通过现有工具查明且确实影响后续任务的信息，再用 ask_user 询问。
        - 每次询问一个聚焦问题。有明确候选时给出易懂的选项；自由输入用空 options，所有问题都允许用户自行输入。多个答案可以同时成立时才设置 multiple=true，不预先替用户选择。
        - 收到回答后把答案用于当前任务并继续；用户跳过时不要原样反复追问。若缺少的答案确实使后续操作无法确定，清楚说明哪些部分仍待决定。
        - ask_user 只收集信息，不授予操作权限，也不替代启动器的权限审批、具体变更审核或效果核对。操作许可交给现有工具及宿主审批流程，不通过 ask_user 索取“批准”或让用户手工执行确认命令。
        - 不需要用户回答时正常完成任务，不为了展示工具而提问；不要索取密码、API key 或其他凭据。
        """;

    private readonly AiChatStore _store = new(paths);
    private string FileFor(string conversation, string turn, string prompt) => Path.Combine(paths.Root, "ai", "conversations",
        AiChatStore.CheckId(conversation), "questions", AiChatStore.CheckId(turn), AiChatStore.CheckId(prompt) + ".json");

    /// <summary>Checks the durable question receipt so accepted answers cannot be presented or routed as still pending.</summary>
    internal bool IsPending(string conversation, string turn, string prompt)
    {
        var file = FileFor(conversation, turn, prompt);
        var stored = JsonFiles.ReadObject(file);
        return stored["state"]?.ToString() == "pending" && stored["response"] is null;
    }

    /// <summary>Offers a source-generated question schema to the existing MEAI function loop.</summary>
    public static AIFunctionDeclaration Tool() => AiToolDeclarations.Create("ask_user",
        "主动向用户询问继续任务所必需的信息或偏好，并等待明确回答或跳过；不要用普通回复中的问题清单代替本工具，也不要等待用户提醒调用。每次一个聚焦问题，options为空表示自由输入，有候选时提供清楚选项；始终允许自由输入。先用已授权的只读工具查询已有事实，不额外索要这类查询的许可。回答不授予操作权限，操作许可使用宿主既有审批流程。不要索取凭据或无新情况地重复已跳过的问题。",
        AiUserPromptJsonContext.Default.AiUserQuestion.GetJsonSchemaAsNode(new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true }).AsObject());

    /// <summary>Publishes the question durably and waits for an explicit response or turn cancellation.</summary>
    public async Task<AiUserAnswerResult> WaitAsync(JsonObject turn, string invocation, AiUserQuestion question,
        Func<string, string?, JsonObject, Task> emit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(question.Question) || question.Options.Any(option => string.IsNullOrWhiteSpace(option.Id) || string.IsNullOrWhiteSpace(option.Label))
            || question.Options.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count() != question.Options.Length)
        {
            throw new ArgumentException("Supply a nonempty question and uniquely identified, nonempty choices.");
        }

        var cid = turn["conversationId"]!.ToString();
        var tid = turn["id"]!.ToString();
        var file = FileFor(cid, tid, invocation);
        var pending = JsonSerializer.SerializeToNode(new AiUserPrompt(invocation, question), AiUserPromptJsonContext.Default.AiUserPrompt)!.AsObject();
        await JsonFiles.WriteAsync(file, new JsonObject { ["prompt"] = pending.DeepClone(), ["state"] = "pending" }, ct).ConfigureAwait(false);
        var previous = turn["status"]!.ToString();
        turn["pendingUserPrompt"] = pending;
        turn["status"] = "awaiting_user";
        try
        {
            await _store.SaveTurnAsync(turn, ct).ConfigureAwait(false);
            await emit("user_prompt_required", invocation, pending).ConfigureAwait(false);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                JsonObject stored;
                using (await ResourceLease.AcquireAsync(file, ct).ConfigureAwait(false)) stored = JsonFiles.ReadObject(file);
                if (!JsonNode.DeepEquals(stored["prompt"], pending))
                    throw new LauncherOperationException("AI_USER_PROMPT_CHANGED", "The question changed; no answer was consumed.");
                if (stored["response"] is JsonObject answer)
                {
                    await emit("user_prompt_answered", invocation, answer).ConfigureAwait(false);
                    return new AiUserAnswerResult(answer.Deserialize(AiJsonContext.Default.AiUserAnswer)
                        ?? throw new InvalidDataException("The stored answer is empty."));
                }

                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            turn.Remove("pendingUserPrompt");
            turn["status"] = previous;
            await _store.SaveTurnAsync(turn, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Validates and records one exact response; repeated identical submissions return the existing receipt.</summary>
    public async Task<AiUserAnswer> RespondAsync(AiUserResponse request, CancellationToken ct)
    {
        var file = FileFor(request.ConversationId, request.TurnId, request.PromptId);
        using var lease = await ResourceLease.AcquireAsync(file, ct).ConfigureAwait(false);
        var stored = JsonFiles.ReadObject(file);
        var prompt = stored["prompt"]!.Deserialize(AiUserPromptJsonContext.Default.AiUserPrompt)!;
        if (prompt.Id != request.PromptId) throw new LauncherOperationException("AI_USER_PROMPT_CHANGED", "Answer the exact current question.");
        var ids = request.OptionIds;
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || (!prompt.Question.Multiple && ids.Length > 1)
            || ids.Any(id => !prompt.Question.Options.Any(option => option.Id == id))
            || (request.Skip ? ids.Length > 0 || !string.IsNullOrWhiteSpace(request.Text) : ids.Length == 0 && string.IsNullOrWhiteSpace(request.Text)))
        {
            throw new ArgumentException("Submit valid choices or your own text, or explicitly skip without an answer.");
        }

        var response = new AiUserAnswer
        {
            PromptId = prompt.Id,
            Question = prompt.Question.Question,
            Status = request.Skip ? "skipped" : "answered",
            Choices = [.. prompt.Question.Options.Where(option => ids.Contains(option.Id, StringComparer.Ordinal)).Select(option => new AiUserAnswerChoice(option.Id, option.Label))],
            Text = request.Text
        };
        var responseDocument = JsonSerializer.SerializeToNode(response, AiJsonContext.Default.AiUserAnswer)!;
        if (stored["response"] is { } prior)
        {
            if (JsonNode.DeepEquals(prior, responseDocument)) return response;
            throw new LauncherOperationException("AI_USER_RESPONSE_CONFLICT", "This question already has a different answer.");
        }

        var turn = _store.GetTurnDocument(request.ConversationId, request.TurnId);
        if (turn["status"]?.ToString() != "awaiting_user" || turn["pendingUserPrompt"]?["id"]?.ToString() != request.PromptId
            || _store.HasCancellationSignal(request.ConversationId, request.TurnId)
            || !JsonNode.DeepEquals(turn["pendingUserPrompt"], stored["prompt"]) || !AiGenerationLease.OwnerAlive(turn))
        {
            throw new LauncherOperationException("AI_USER_PROMPT_INACTIVE", "This question is no longer waiting for an answer.");
        }

        stored["response"] = responseDocument;
        stored["state"] = response.Status;
        await JsonFiles.WriteAsync(file, stored, ct).ConfigureAwait(false);
        return response;
    }
}