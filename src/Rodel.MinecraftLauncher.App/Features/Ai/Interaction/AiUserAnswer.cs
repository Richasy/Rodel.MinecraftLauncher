// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application.AI;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Retains an unsubmitted answer across polling and navigation; Core alone accepts the final response.</summary>
internal sealed class AiUserAnswer(
    AiUiState state,
    AiUiConversation conversation,
    AiUiTurn turn,
    AiUserPrompt prompt)
{
    internal AiUserPrompt Prompt => prompt;
    internal HashSet<string> Selected { get; } = [with(StringComparer.Ordinal)];
    internal string Text { get; private set; } = string.Empty;
    internal string Error { get; private set; } = string.Empty;
    internal bool Working { get; private set; }
    internal bool Submitted { get; private set; }
    internal bool CanSubmit => !Working && !Submitted && (Selected.Count > 0 || !string.IsNullOrWhiteSpace(Text));

    internal void ChangeText(string value)
    {
        Text = value;
        state.Notify();
    }

    internal void Toggle(string id)
    {
        if (Working || Submitted)
            return;
        if (!Selected.Remove(id))
        {
            if (!prompt.Question.Multiple)
                Selected.Clear();
            Selected.Add(id);
        }

        state.Notify();
    }

    internal async Task SubmitAsync(bool skip)
    {
        if (Working || Submitted || (!skip && !CanSubmit))
            return;
        Working = true;
        Error = string.Empty;
        state.Notify();
        try
        {
            await state.Service.RespondToUserPromptAsync(new()
            {
                ConversationId = conversation.Id,
                TurnId = turn.Id,
                PromptId = prompt.Id,
                OptionIds = skip ? [] : [.. prompt.Question.Options.Where(option => Selected.Contains(option.Id)).Select(option => option.Id)],
                Text = skip ? string.Empty : Text,
                Skip = skip
            });
            Submitted = true;
        }
        catch (Exception error)
        {
            Error = error.Message;
        }
        finally
        {
            Working = false;
            state.Notify();
        }
    }

    internal static AiUserAnswer? Capture(AiUiState state, AiUiConversation conversation, AiUiTurn turn) => turn.Record is { Status: "awaiting_user", PendingUserPrompt: { } prompt }
        ? turn.UserAnswer is { } existing && existing.Prompt.Id == prompt.Id ? existing : new(state, conversation, turn, prompt)
        : null;
}