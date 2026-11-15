// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Reads one UI observation without dropping events committed between the first event page and a terminal turn snapshot.</summary>
internal static class AiTurnObservation
{
    internal static bool NeedsAcknowledgement(JsonNode? effect) => effect is not null && effect["acknowledgement"]?["acceptedUnknown"]?.GetValue<bool>() != true;

    internal static bool HasPendingEffects(AiChatRecord turn) => turn.UnknownEffects?.Any(NeedsAcknowledgement) == true;

    internal static bool HasEffectProblem(AiChatRecord turn) => AiChatStates.IsTerminal(turn.Status) && turn.Intents?.Any(intent => intent?["state"]?.ToString() == "unknown_effect" && NeedsAcknowledgement(intent) && intent["verification"] is { } verification && verification["operation"]?["state"]?.ToString() is not ("queued" or "applying")) == true;

    internal static async Task<(AiChatRecord Turn, AiChatEvent[] Events, long NextSequence)> ReadAsync(long after, Func<long, Task<AiEventPage>> readEvents, Func<Task<AiChatRecord>> readTurn)
    {
        var events = new List<AiChatEvent>();
        var cursor = after;
        async Task DrainAsync()
        {
            AiEventPage page;
            do
            {
                page = await readEvents(cursor).ConfigureAwait(false);
                foreach (var item in page.Items)
                {
                    if (item.Sequence > cursor)
                        events.Add(item);
                }

                cursor = page.NextSequence;
            }
            while (page.HasMore);
        }

        await DrainAsync().ConfigureAwait(false);
        var turn = await readTurn().ConfigureAwait(false);
        if (AiChatStates.IsTerminal(turn.Status))
            await DrainAsync().ConfigureAwait(false);
        return (turn, [.. events], cursor);
    }
}