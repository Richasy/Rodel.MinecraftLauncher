// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiContextArchives(LauncherPaths paths)
{
    private string FileFor(string conversation, string archive) => Path.Combine(paths.Root, "ai", "conversations",
        AiChatStore.CheckId(conversation), "context-archives", AiChatStore.CheckId(archive) + ".json");

    public async Task<AiContextArchive> SaveAsync(JsonObject turn, JsonArray messages, CancellationToken ct)
    {
        var previous = messages.Where(item => item?["contextCheckpoint"]?["archiveId"] is not null)
            .Select(item => item!["contextCheckpoint"]!["archiveId"]!.ToString()).Distinct(StringComparer.Ordinal).ToArray();
        var archive = new AiContextArchive(1, Guid.NewGuid().ToString("N"), turn["conversationId"]!.ToString(),
            turn["id"]!.ToString(), DateTimeOffset.UtcNow.ToString("O"), Hash(messages), messages.DeepClone().AsArray(), previous);
        await JsonFiles.WriteAsync(FileFor(archive.ConversationId, archive.Id),
            JsonSerializer.SerializeToNode(archive, AiContextJsonContext.Default.AiContextArchive)!, ct).ConfigureAwait(false);
        return archive;
    }

    /// <summary>Reads a Unicode-safe character page of a validated durable context archive.</summary>
    public AiContextPage Read(AiContextRead request)
    {
        if (request.Offset < 0 || request.Characters is < 256 or > 16000 || request.MessageIndex is < 0)
            throw new ArgumentException("Use a nonnegative offset/message index and a character page of 256 through 16000.");
        var archive = Load(request.ConversationId, request.ArchiveId);
        var portable = Portable(archive.Messages);
        if (request.MessageIndex is { } index && index >= portable.Count)
            throw new ArgumentException("The requested message index is outside this archive.");
        var text = request.MessageIndex is { } selected
            ? portable[selected]?["text"]?.GetValue<string>() ?? portable[selected]!.ToJsonString()
            : portable.ToJsonString();
        if (request.Offset > text.Length || (request.Offset > 0 && request.Offset < text.Length && char.IsLowSurrogate(text[request.Offset]) && char.IsHighSurrogate(text[request.Offset - 1])))
            throw new ArgumentException("The requested offset is outside the archive text or splits a Unicode scalar.");
        var end = request.Offset + Math.Min(text.Length - request.Offset, request.Characters);
        if (end < text.Length && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end--;
        var page = new AiContextPage(1, archive.Id, archive.ContentHash, text[request.Offset..end],
            request.Offset, end < text.Length ? end : null, end < text.Length, text.Length, portable.Count, request.MessageIndex);
        return page;
    }

    internal AiContextArchive Load(string conversationId, string archiveId)
    {
        _ = new AiChatStore(paths).Get(conversationId);
        AiContextArchive archive;
        try
        {
            archive = JsonFiles.ReadObject(FileFor(conversationId, archiveId))
                .Deserialize(AiContextJsonContext.Default.AiContextArchive) ?? throw new InvalidDataException("The context archive is empty.");
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new LauncherOperationException("AI_CONTEXT_ARCHIVE_NOT_FOUND", "No context archive has this identifier in the current conversation.");
        }

        if (archive.SchemaVersion != 1 || !string.Equals(archive.Id, archiveId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(archive.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase)
            || archive.ContentHash != Hash(archive.Messages))
        {
            throw new LauncherOperationException("AI_CONTEXT_ARCHIVE_INVALID", "The context archive version, identity or content hash is inconsistent. No reconstructed content was returned.");
        }

        return archive;
    }

    public static JsonArray Portable(JsonArray messages)
    {
        var result = new JsonArray();
        foreach (var item in messages)
        {
            if (item is not JsonObject message) throw new InvalidDataException("A context archive contains a non-message value.");
            var portable = new JsonObject();
            foreach (var field in new[] { "role", "text", "name", "callId", "calls", "partial" })
                if (message.TryGetPropertyValue(field, out var value)) portable[field] = value?.DeepClone();
            result.Add((JsonNode)portable);
        }

        return result;
    }
    public static string Hash(JsonArray messages) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(messages.ToJsonString())));
}