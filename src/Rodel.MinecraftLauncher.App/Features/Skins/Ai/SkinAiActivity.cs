// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Retains either one visible content part or a strictly consecutive sequence of tool calls.</summary>
internal sealed record SkinAiActivityBlock(string Id, AiTranscriptPart? Content, AiTranscriptPart[] Entries);

/// <summary>Projects actual drawing arguments without inventing pixel effects from assistant prose.</summary>
internal sealed record SkinAiToolDetails(
    string? Label,
    SkinSurface? Surface,
    string? PaintLayerId,
    SkinEdit[] Edits,
    SkinViewOptions? View,
    string? Error);

/// <summary>Groups tool activity and identifies superseded attempts independently of the native control tree.</summary>
internal static class SkinAiActivity
{
    internal static SkinAiActivityBlock[] Group(IEnumerable<AiTranscriptPart> parts)
    {
        var blocks = new List<SkinAiActivityBlock>();
        var pending = new List<AiTranscriptPart>();
        void Flush()
        {
            if (pending.Count == 0)
                return;
            blocks.Add(new("tools:" + pending[0].Id, null, [.. pending]));
            pending.Clear();
        }

        foreach (var part in parts)
        {
            if (part.Kind == AiTranscriptPartKind.Tool)
            {
                pending.Add(part);
            }
            else
            {
                Flush();
                blocks.Add(new(part.Id, part, []));
            }
        }

        Flush();
        return [.. blocks];
    }

    internal static bool HasContinuation(AiChatRecord record, IEnumerable<AiChatRecord> turns)
    {
        var goal = record.GoalId ?? record.Id;
        var seen = false;
        foreach (var item in turns)
        {
            if (item.Id == record.Id)
            {
                seen = true;
                continue;
            }

            if (seen && (item.RetryOf == record.Id || (goal is not null && item.GoalId == goal)))
                return true;
        }

        return false;
    }

    internal static SkinAiToolDetails Read(AiTranscriptPart part, IEnumerable<AiTranscriptPart> context)
    {
        try
        {
            var arguments = Object(part.Arguments);
            if (arguments is null && !string.IsNullOrWhiteSpace(part.Arguments))
                throw new JsonException("The recorded tool arguments are not an object.");
            var label = Text(arguments, "label");
            var data = Object(part.Result)?["data"] as JsonObject;
            label ??= Text(data, "label");
            var proposalId = Text(arguments, "proposalId");
            if (label is null && proposalId is not null)
            {
                foreach (var earlier in context.Where(item => item.Name == "skin_propose"))
                {
                    var candidate = Object(earlier.Result)?["data"] as JsonObject;
                    if (Text(candidate, "proposalId") != proposalId)
                        continue;
                    label = Text(candidate, "label") ?? Text(Object(earlier.Arguments), "label");
                    break;
                }
            }

            SkinSurface? surface = null;
            SkinEdit[] edits = [];
            SkinViewOptions? view = null;
            var paintLayer = Text(arguments, "paintLayerId");
            if (part.Name == "skin_propose" && arguments?["edits"] is { } actions)
            {
                edits = actions.Deserialize(SkinJsonContext.Default.SkinEditArray) ?? throw new JsonException("The drawing actions are empty.");
            }
            else if (part.Name == "skin_footprint" && arguments?["request"] is { } footprint)
            {
                edits = [footprint.Deserialize(SkinJsonContext.Default.SkinEdit) ?? throw new JsonException("The drawing action is empty.")];
            }
            else if (part.Name == "skin_read_face" && arguments is not null)
            {
                var address = new JsonObject
                {
                    ["part"] = arguments["part"]?.DeepClone(),
                    ["layer"] = arguments["layer"]?.DeepClone(),
                    ["face"] = arguments["face"]?.DeepClone(),
                };
                surface = address.Deserialize(SkinJsonContext.Default.SkinSurface);
            }
            else if (part.Name is "skin_select" or "skin_capture")
            {
                if (arguments?["request"]?["surface"] is { } address)
                    surface = address.Deserialize(SkinJsonContext.Default.SkinSurface);
                paintLayer = Text(arguments?["request"] as JsonObject, "paintLayerId");
            }
            else if (part.Name == "skin_pick" && data?["hit"] is JsonObject hit && hit["surface"] is { } address)
            {
                surface = address.Deserialize(SkinJsonContext.Default.SkinSurface);
            }

            if (part.Name == "skin_proposal_preview" && arguments?["view"] is { } options)
                view = options.Deserialize(SkinJsonContext.Default.SkinViewOptions);
            return new(
                label,
                surface,
                paintLayer,
                edits,
                view,
                null);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        {
            return new(
                null,
                null,
                null,
                [],
                null,
                error.Message);
        }
    }

    private static JsonObject? Object(string value) => string.IsNullOrWhiteSpace(value) ? null : JsonNode.Parse(value) as JsonObject;

    private static string? Text(JsonObject? value, string key) => value?[key] is JsonValue text && text.TryGetValue<string>(out var result) ? result : null;
}