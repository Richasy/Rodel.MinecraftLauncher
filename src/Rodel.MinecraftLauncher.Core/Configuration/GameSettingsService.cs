// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Configuration;

/// <summary>Local options.txt edits preserve unknown lines, encoding and unrelated settings.</summary>
public sealed class GameSettingsService(LauncherPaths paths)
{
    internal static string ResolveLaunchLanguage(string minecraft, JsonObject metadata, string language)
    {
        var normalized = language.Replace('-', '_').ToLowerInvariant();
        var parts = normalized.Split('_');
        if (parts.Length != 2 || parts[0].Length is < 2 or > 3 || parts[1].Length != 2
            || parts.Any(part => !part.All(char.IsAsciiLetter)))
        {
            throw new ArgumentException("Game language must be a Minecraft locale such as zh_cn, en_us or ar_sa.", nameof(language));
        }

        var id = metadata["assetIndex"]?["id"]?.GetValue<string>() ?? metadata["assets"]?.GetValue<string>() ?? "legacy";
        var indexPath = SharedRuntimeLayout.Resolve(minecraft, "assets/indexes/" + id + ".json");
        if (File.Exists(indexPath) && JsonFiles.ReadObject(indexPath)["objects"] is JsonObject objects)
        {
            var names = objects.Select(entry => entry.Key).Where(name => name.StartsWith("minecraft/lang/", StringComparison.Ordinal))
                .Select(Path.GetFileNameWithoutExtension).ToArray();
            if (names.FirstOrDefault(name => string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase)) is { } actual)
                return actual;
            // Older clients use country codes such as zh_CN; English itself is bundled in the client.
            if (names.Any(name => name is not null && name.Any(char.IsAsciiLetterUpper)))
                return parts[0] + "_" + parts[1].ToUpperInvariant();
        }

        return normalized;
    }

    // The session monitor holds the game-directory preparation lease; ordinary SetAsync would defer its own queued session.
    internal static async Task ApplyLaunchLanguageAsync(string game, string language, CancellationToken ct)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: Path.Combine(game, "options.txt"), cancellationToken: ct).ConfigureAwait(false);
        var document = await ReadAsync(game, null, ct).ConfigureAwait(false);
        if (document.Values()["lang"]?.ToString() != language)
            await WriteAsync(document, document.Edit(new JsonObject { ["lang"] = language }), ct).ConfigureAwait(false);
    }

    private sealed record Document(string Path, byte[] Bytes, Encoding Encoding, byte[] Bom, string[] Lines, string[] Endings)
    {
        public string Fingerprint => Convert.ToHexStringLower(SHA256.HashData(Bytes));

        public JsonObject Values()
        {
            var values = new JsonObject();
            foreach (var line in Lines)
            {
                if (line.IndexOf(':') is var colon && colon > 0)
                    values[line[..colon]] = line[(colon + 1)..];
            }

            return values;
        }

        public byte[] Edit(JsonObject changes)
        {
            var lines = Lines.ToList();
            var endings = Endings.ToList();
            var newline = endings.FirstOrDefault(x => x.Length > 0) ?? "\n";
            foreach (var (key, value) in changes)
            {
                if (string.IsNullOrEmpty(key) || key.IndexOfAny([':', '\r', '\n', '\0']) >= 0 || value?.ToString().IndexOfAny(['\r', '\n', '\0']) >= 0)
                    throw new ArgumentException("Game setting keys/values must fit one options.txt line.");
                var matches = Enumerable.Range(0, lines.Count).Where(i => lines[i].StartsWith(key + ":", StringComparison.Ordinal)).ToArray();
                if (value is null)
                {
                    foreach (var i in matches.Reverse())
                    {
                        lines.RemoveAt(i);
                        endings.RemoveAt(i);
                    }

                    continue;
                }

                if (matches.Length > 0)
                {
                    foreach (var i in matches)
                        lines[i] = key + ":" + value.ToString();
                }
                else
                {
                    if (endings.Count > 0 && endings[^1].Length == 0)
                        endings[^1] = newline;
                    lines.Add(key + ":" + value);
                    endings.Add(newline);
                }
            }

            var text = string.Concat(lines.Select((line, i) => line + endings[i]));
            return [.. Bom, .. Encoding.GetBytes(text)];
        }
    }

    private static async Task<Document> ReadAsync(string game, string? encoding, CancellationToken ct)
    {
        var file = Path.Combine(game, "options.txt");
        var bytes = File.Exists(file) ? await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false) : [];
        if (bytes.Length > 16 * 1024 * 1024)
            throw new IOException("options.txt exceeds the editing budget; the game file remains untouched.");
        byte[] bom = [];
        Encoding selected;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
        {
            bom = [0xef, 0xbb, 0xbf];
            selected = new UTF8Encoding(false, true);
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }))
        {
            bom = [0xff, 0xfe];
            selected = new UnicodeEncoding(false, false, true);
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }))
        {
            bom = [0xfe, 0xff];
            selected = new UnicodeEncoding(true, false, true);
        }
        else
        {
            selected = new UTF8Encoding(false, true);
            try
            {
                selected.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                selected = LogEncoding.Resolve(encoding ?? LogEncoding.SystemEncoding, out _);
            }

            if (encoding is not null)
                selected = LogEncoding.Resolve(encoding, out var warning) is { } candidate && warning is null ? candidate : throw new ArgumentException(warning);
            selected = Encoding.GetEncoding(selected.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }

        var text = selected.GetString(bytes, bom.Length, bytes.Length - bom.Length);
        if (!selected.GetBytes(text).AsSpan().SequenceEqual(bytes.AsSpan(bom.Length)))
            throw new IOException("The options encoding cannot be round-tripped; supply its actual --encoding.");
        var lines = new List<string>();
        var endings = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n'))
                continue;
            lines.Add(text[start..i]);
            var end = i + 1;
            if (text[i] == '\r' && end < text.Length && text[end] == '\n')
                end++;
            endings.Add(text[i..end]);
            i = end - 1;
            start = end;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
            endings.Add(string.Empty);
        }

        return new(file, bytes, selected, bom, [.. lines], [.. endings]);
    }

    /// <summary>Reads the game options and deferred edits, retaining unreadable pending data with an explicit warning.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Retain the public service instance API for callers and future instance configuration.")]
    public async Task<GameSettingsSnapshot> GetAsync(string game, string? encoding = null, CancellationToken ct = default)
    {
        var document = await ReadAsync(game, encoding, ct).ConfigureAwait(false);
        var pending = Path.Combine(game, ".rodel", "game-settings", "pending.json");
        var result = new GameSettingsSnapshot
        {
            Path = document.Path,
            Exists = File.Exists(document.Path),
            Encoding = document.Encoding.WebName,
            Fingerprint = document.Fingerprint,
            Values = document.Values(),
            Pending = null
        };
        try
        {
            if (File.Exists(pending))
                result = result with { Pending = ReadPending(pending) };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            var warning = "Pending settings are unreadable and retained for recovery: " + ex.Message;
            result = result with { PendingWarning = warning, Warnings = new JsonArray(warning) };
        }

        return result;
    }

    private static JsonObject ReadPending(string file)
    {
        var pending = JsonFiles.ReadObject(file);
        if (pending["changes"] is not JsonObject || pending["baseValues"] is not JsonObject)
            throw new InvalidDataException("Pending settings must contain changes and baseValues objects.");
        return pending;
    }

    /// <summary>Retains pending game-setting edits in a recovery file without applying them to options.txt.</summary>
    /// <param name="game">The target game's directory, independent of the launcher data root.</param>
    /// <param name="ct">Cancels while waiting for the exclusive game-settings lease.</param>
    /// <returns>The discarded recovery path, or a nothing-pending result when no edits are queued.</returns>
    public static async Task<GameSettingsChangeResult> DiscardPendingAsync(string game, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: Path.Combine(game, "options.txt"), cancellationToken: ct).ConfigureAwait(false);
        var file = Path.Combine(game, ".rodel", "game-settings", "pending.json");
        if (!File.Exists(file))
        {
            return new() { State = "nothing-pending" };
        }

        var retained = Path.Combine(Path.GetDirectoryName(file)!, "discarded-" + Guid.NewGuid().ToString("N") + ".json");
        File.Move(file, retained);
        return new() { State = "discarded", RecoveryPath = retained };
    }

    /// <summary>Translate only file identities owned by a completed content operation, including queued choices.</summary>
    public async Task<GameSettingsChangeResult> TransformResourcePacksAsync(string game, IReadOnlyList<ResourcePackMapping> mappings, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: Path.Combine(game, "options.txt"), cancellationToken: ct).ConfigureAwait(false);
        var document = await ReadAsync(game, null, ct).ConfigureAwait(false);
        var values = document.Values();
        var pendingFile = Path.Combine(game, ".rodel", "game-settings", "pending.json");
        var pending = File.Exists(pendingFile) ? ReadPending(pendingFile) : null;
        var changes = new JsonObject();
        var active = new SessionService(paths).HasActiveGame(game);
        string? Transform(JsonNode? raw)
        {
            if (raw is null)
                return null;
            var entries = JsonNode.Parse(raw.ToString()) as JsonArray ?? throw new InvalidDataException("Resource pack selection must be an array.");
            var output = new JsonArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in entries)
            {
                if (item is not JsonValue text || !text.TryGetValue<string>(out var original))
                    throw new InvalidDataException("Resource pack entries must be strings.");
                var entry = original;
                foreach (var mapping in mappings)
                {
                    var oldName = mapping.OldName;
                    if (entry == oldName || entry == "file/" + oldName)
                        entry = mapping.NewName is { } next ? (entry.StartsWith("file/", StringComparison.Ordinal) ? "file/" : string.Empty) + next : null;
                }

                if (entry is not null && seen.Add(entry))
                    output.Add((JsonNode?)JsonValue.Create(entry));
            }

            return JsonNode.DeepEquals(entries, output) ? raw.ToString() : output.ToJsonString();
        }

        // Validate all lists before any write. Existing pending choices take precedence when the game is active.
        foreach (var key in new[]
        {
            "resourcePacks",
            "incompatibleResourcePacks"
        }

        )
        {
            var actual = values[key];
            var translated = Transform(actual);
            if (translated != actual?.ToString())
                changes[key] = translated;
            if (pending?["changes"] is JsonObject queued && queued.ContainsKey(key))
            {
                var translatedPending = Transform(queued[key]);
                var translatedBase = active ? pending["baseValues"]?[key]?.ToString() : Transform(pending["baseValues"]?[key]);
                queued[key] = translatedPending;
                pending["baseValues"]![key] = translatedBase;
            }
        }

        if (active && changes.Count > 0)
        {
            pending ??= new JsonObject
            {
                ["changes"] = new JsonObject(),
                ["baseValues"] = new JsonObject(),
                ["encoding"] = document.Encoding.WebName
            };
            foreach (var pair in changes)
            {
                if (!pending["changes"]!.AsObject().ContainsKey(pair.Key))
                {
                    pending["changes"]![pair.Key] = pair.Value?.DeepClone();
                    pending["baseValues"]![pair.Key] = values[pair.Key]?.DeepClone();
                }
            }
        }
        else if (changes.Count > 0)
        {
            await WriteAsync(document, document.Edit(changes), ct).ConfigureAwait(false);
        }

        if (pending is not null)
            await JsonFiles.WriteAsync(pendingFile, pending, ct).ConfigureAwait(false);
        return new GameSettingsChangeResult
        {
            State = active && (changes.Count > 0 || pending is not null) ? "deferred" : "applied",
            Changes = changes
        };
    }

    public async Task<GameSettingsChangeResult> SetAsync(string game, JsonObject changes, bool plan = false, string? expected = null, string? encoding = null, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: Path.Combine(game, "options.txt"), cancellationToken: ct).ConfigureAwait(false);
        var document = await ReadAsync(game, encoding, ct).ConfigureAwait(false);
        var output = document.Edit(changes);
        var result = new GameSettingsChangeResult
        {
            Path = document.Path,
            Fingerprint = document.Fingerprint,
            Encoding = document.Encoding.WebName,
            Changes = changes.DeepClone().AsObject(),
            State = "planned"
        };
        if (expected is not null && expected != document.Fingerprint)
        {
            result.State = "conflict";
            return result;
        }

        if (plan)
            return result;
        var pending = Path.Combine(game, ".rodel", "game-settings", "pending.json");
        if (new SessionService(paths).HasActiveGame(game))
        {
            var queued = File.Exists(pending) ? ReadPending(pending) : new JsonObject
            {
                ["changes"] = new JsonObject(),
                ["baseValues"] = new JsonObject()
            };
            var values = document.Values();
            foreach (var pair in changes)
            {
                if (!queued["changes"]!.AsObject().ContainsKey(pair.Key))
                    queued["baseValues"]![pair.Key] = values[pair.Key]?.DeepClone();
                queued["changes"]![pair.Key] = pair.Value?.DeepClone();
            }

            queued["encoding"] = document.Encoding.WebName;
            await JsonFiles.WriteAsync(pending, queued, ct).ConfigureAwait(false);
            result.State = "deferred";
            result.Reason = "Game is active; apply after exit to avoid losing game edits.";
            return result;
        }

        await WriteAsync(document, output, ct).ConfigureAwait(false);
        result.State = "applied";
        result.NewFingerprint = Convert.ToHexStringLower(SHA256.HashData(output));
        if (File.Exists(pending))
        {
            try
            {
                var queued = ReadPending(pending);
                foreach (var key in changes.Select(x => x.Key))
                {
                    queued["changes"]?.AsObject().Remove(key);
                    queued["baseValues"]?.AsObject().Remove(key);
                }

                if (queued["changes"] is JsonObject remaining && remaining.Count > 0)
                    await JsonFiles.WriteAsync(pending, queued, ct).ConfigureAwait(false);
                else
                    File.Delete(pending);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
            {
                result.Warning = "Options applied, but stale pending-edit cleanup failed: " + ex.Message;
            }
        }

        return result;
    }

    public async Task<GameSettingsChangeResult> ApplyPendingAsync(string game, bool overwrite = false, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: Path.Combine(game, "options.txt"), cancellationToken: ct).ConfigureAwait(false);
        var file = Path.Combine(game, ".rodel", "game-settings", "pending.json");
        if (!File.Exists(file))
        {
            return new() { State = "nothing-pending" };
        }

        if (new SessionService(paths).HasActiveGame(game))
        {
            return new() { State = "deferred", Reason = "Game is active." };
        }

        var pending = ReadPending(file);
        var document = await ReadAsync(game, pending["encoding"]?.ToString(), ct).ConfigureAwait(false);
        var values = document.Values();
        var conflicts = new List<string>();
        foreach (var pair in pending["changes"]!.AsObject())
        {
            if (!JsonNode.DeepEquals(values[pair.Key], pending["baseValues"]?[pair.Key]) && !JsonNode.DeepEquals(values[pair.Key], pair.Value))
                conflicts.Add(pair.Key);
        }

        if (!overwrite && conflicts.Count > 0)
        {
            return new() { State = "conflict", Conflicts = conflicts, Pending = pending };
        }

        await WriteAsync(document, document.Edit(pending["changes"]!.AsObject()), ct).ConfigureAwait(false);
        File.Delete(file);
        return new() { State = "applied", Changes = pending["changes"]!.DeepClone().AsObject(), ConflictsOverwritten = conflicts };
    }

    private static async Task WriteAsync(Document document, byte[] bytes, CancellationToken ct)
    {
        var current = File.Exists(document.Path) ? await File.ReadAllBytesAsync(document.Path, ct).ConfigureAwait(false) : [];
        if (!current.AsSpan().SequenceEqual(document.Bytes))
            throw new IOException("Game settings changed during this edit; retry against the current file.");
        Directory.CreateDirectory(Path.GetDirectoryName(document.Path)!);
        var temp = document.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            {
                var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough);
                await using (file.ConfigureAwait(false))
                {
                    await file.WriteAsync(bytes, ct).ConfigureAwait(false);
                    await file.FlushAsync(ct).ConfigureAwait(false);
                    file.Flush(true);
                }
            }

            ct.ThrowIfCancellationRequested();
            if (File.Exists(document.Path))
                File.Replace(temp, document.Path, null);
            else
                File.Move(temp, document.Path);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}