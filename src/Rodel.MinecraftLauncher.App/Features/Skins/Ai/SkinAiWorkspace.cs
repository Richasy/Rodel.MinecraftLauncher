// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.Core.Application.AI;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Retains one project's current conversation and replaces only exactly reviewed history.</summary>
internal sealed class SkinAiWorkspace(string documentId, AiUiState ui)
{
    internal string DocumentId { get; } = documentId;
    internal AiUiState Ui { get; } = ui;
    internal bool Preparing { get; set; }
    internal bool Refreshing { get; set; }
    internal bool ReloadPending { get; set; }
    internal string ReloadKey { get; set; } = string.Empty;
    internal string Error { get; set; } = string.Empty;

    internal async Task<bool> RestartAsync(AiUiConversation previous, AiConversationRemovalReview? review)
    {
        if (Preparing || Ui.Busy || !ReferenceEquals(Ui.Current, previous))
            return false;
        Preparing = true;
        Error = string.Empty;
        Ui.Notify();
        try
        {
            return await Ui.RunAsync(async () =>
            {
                // Core stops active/queued turns and removes the exact reviewed chat. It does not touch the skin.
                await Ui.Chats.RemoveAsync(previous, review);
                var next = new AiUiConversation
                {
                    SkinScopeId = previous.SkinScopeId,
                    ModelId = previous.ModelId ?? Ui.Configuration.Defaults.ModelId,
                    Files = false,
                    Network = false
                };
                Ui.Conversations.Clear();
                Ui.Conversations.Add(next);
                ReloadKey = string.Empty;
                // Cancellation may finish a committed drawing step after its last UI poll.
                ReloadPending = true;
                Ui.Select(next);
            });
        }
        finally
        {
            Preparing = false;
            Ui.Notify();
        }
    }
}