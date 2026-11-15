// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Appends durable JSON lines and indexes their byte offsets for sequence-based reads.</summary>
internal sealed class AiEventJournal
{
    private const int EventLimit = 10000;
    private const int CachedConversations = 16;
    private const int BufferSize = 64 * 1024;
    private readonly Dictionary<string, JournalIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);

    internal async Task AppendAsync(string file, string turnId, JsonObject item, CancellationToken ct)
    {
        using var lease = await ResourceLease.AcquireAsync(file, ct).ConfigureAwait(false);
        using var stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, BufferSize);
        var index = ReadIndex(file, stream, ct);
        var sequence = (index.Turns.GetValueOrDefault(turnId)?.Count ?? 0) + 1L;
        if (sequence > EventLimit)
            throw new LauncherOperationException("AI_EVENT_BUDGET_EXCEEDED", "This turn exceeded its durable event budget.");

        var start = index.End;
        item["sequence"] = sequence;
        var bytes = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(bytes))
            item.WriteTo(writer);

        // After this point finish the commit or roll back its suffix even if the caller is canceled.
        ct.ThrowIfCancellationRequested();
        if (stream.Length != start)
            stream.SetLength(start);
        stream.Position = start;
        try
        {
            stream.Write(bytes.WrittenSpan);
            stream.WriteByte((byte)'\n');
            stream.Flush(flushToDisk: true);
        }
        catch (IOException)
        {
            stream.SetLength(start);
            stream.Flush(flushToDisk: true);
            throw;
        }

        index.Add(turnId, sequence, start, bytes.WrittenCount);
    }

    internal async Task<(long Next, bool HasMore)> ReadAsync(
        string file, string turnId, long after, int limit, Action<JsonObject> visit, CancellationToken ct)
    {
        using var lease = await ResourceLease.AcquireAsync(file, ct).ConfigureAwait(false);
        using var stream = Open(file);
        if (stream is null)
            return (after, false);

        var index = ReadIndex(file, stream, ct);
        if (!index.Turns.TryGetValue(turnId, out var records) || after >= records.Count)
            return (after, false);

        var start = (int)after;
        var end = start + Math.Min(records.Count - start, limit);
        for (var i = start; i < end; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (offset, length) = records[i];
            stream.Position = offset;
            var buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                stream.ReadExactly(buffer.AsSpan(0, length));
                if (stream.ReadByte() != '\n' || JsonNode.Parse(buffer.AsSpan(0, length)) is not JsonObject item)
                {
                    throw new InvalidDataException("The AI event journal has an invalid committed record.");
                }

                visit(item);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
        }

        return (end, end < records.Count);
    }

    private JournalIndex ReadIndex(string file, FileStream stream, CancellationToken ct)
    {
        // The file lease serializes both commit and indexing across processes and across turns.
        JournalIndex index;
        lock (_indexes)
        {
            if (!_indexes.TryGetValue(file, out index!))
            {
                if (_indexes.Count >= CachedConversations)
                    _indexes.Remove(_indexes.Keys.First());
                _indexes.Add(file, index = new());
            }

            if (stream.Length < index.End)
            {
                index = new();
                _indexes[file] = index;
            }
        }

        var position = index.End;
        if (position == stream.Length)
            return index;

        stream.Position = position;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var pending = new ArrayBufferWriter<byte>();
        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, BufferSize)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                var remaining = buffer.AsSpan(0, read);
                while (remaining.IndexOf((byte)'\n') is var newline && newline >= 0)
                {
                    ReadOnlySpan<byte> line = remaining[..newline];
                    if (pending.WrittenCount != 0)
                    {
                        pending.Write(line);
                        line = pending.WrittenSpan;
                    }

                    var (turnId, sequence) = ReadIdentity(line);
                    index.Add(turnId, sequence, index.End, line.Length);
                    pending.Clear();
                    remaining = remaining[(newline + 1)..];
                }

                pending.Write(remaining);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }

        // A killed writer may leave bytes after the last newline. Readers ignore that suffix.
        return index;
    }

    private static (string TurnId, long Sequence) ReadIdentity(ReadOnlySpan<byte> line)
    {
        var reader = new Utf8JsonReader(line);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new InvalidDataException("The AI event journal record must be a JSON object.");

        string? turnId = null;
        long sequence = 0;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var turnField = reader.ValueTextEquals("turnId"u8);
            var sequenceField = reader.ValueTextEquals("sequence"u8);
            if (!reader.Read())
                throw new InvalidDataException("The AI event journal record is incomplete.");
            if (turnField && reader.TokenType == JsonTokenType.String)
                turnId = reader.GetString();
            if (sequenceField && reader.TokenType == JsonTokenType.Number)
                reader.TryGetInt64(out sequence);
            reader.Skip();
        }

        if (reader.TokenType != JsonTokenType.EndObject || reader.Read() || !Guid.TryParseExact(turnId, "N", out _))
            throw new InvalidDataException("The AI event journal record has an invalid turn identity.");
        return (turnId, sequence);
    }

    private static FileStream? Open(string file)
    {
        try { return new(file, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    private sealed class JournalIndex
    {
        internal Dictionary<string, List<(long Offset, int Length)>> Turns { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal long End { get; private set; }

        internal void Add(string turnId, long sequence, long offset, int length)
        {
            if (!Turns.TryGetValue(turnId, out var records))
                Turns.Add(turnId, records = []);
            if (sequence != records.Count + 1L || sequence > EventLimit)
                throw new InvalidDataException("The AI event journal record has an invalid turn sequence.");
            records.Add((offset, length));
            End = offset + length + 1;
        }
    }
}