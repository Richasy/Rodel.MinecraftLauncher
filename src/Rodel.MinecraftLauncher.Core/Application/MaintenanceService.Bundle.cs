// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Launch;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class MaintenanceService
{
    /// <summary>Previews or publishes a diagnostic bundle, retaining source availability and redaction limits.</summary>
    public async Task<DiagnosticBundleManifest> ExportBundleAsync(string sessionId, string? destination = null, bool redact = true, CancellationToken ct = default)
    {
        var session = new SessionService(paths).Get(sessionId);
        var state = JsonSerializer.SerializeToNode(session, LaunchJsonContext.Default.SessionStatus)!.AsObject();
        state.Remove("arguments");
        state.Remove("environmentOverrides");
        var diagnosis = await DiagnoseSessionAsync(sessionId, ct).ConfigureAwait(false);
        var inventory = new List<DiagnosticBundleEntry>();
        var warnings = new JsonArray();
        var sources = new List<(string Name, string Path, string? Encoding)>();
        var snapshots = await new LogService(paths).SnapshotAsync(sessionId, ct).ConfigureAwait(false);
        foreach (var (key, snapshot) in snapshots)
        {
            inventory.Add(new()
            {
                Entry = "logs/" + key + ".log",
                Source = snapshot.Source,
                Available = snapshot.Source != "unavailable",
                Bytes = snapshot.Bytes.Length,
                Offset = snapshot.Offset,
                TotalBytes = snapshot.TotalBytes,
                Truncated = snapshot.Offset > 0 || snapshot.Offset + snapshot.Bytes.Length < snapshot.TotalBytes
            });
            if (snapshot.Warning is not null) warnings.Add((JsonNode?)JsonValue.Create(snapshot.Warning));
        }

        foreach (var report in diagnosis.CrashReports)
        {
            if (report.Path is { } file)
                sources.Add(("reports/" + sources.Count.ToString("D3") + "-" + Path.GetFileName(file), file, report.Encoding ?? session.FileEncoding));
        }

        inventory.Add(new() { Entry = "session.json", Generated = true });
        inventory.Add(new() { Entry = "diagnosis.json", Generated = true });
        inventory.Add(new() { Entry = "launcher-activity.json", Generated = true });
        foreach (var source in sources)
        {
            try
            {
                inventory.Add(new() { Entry = source.Name, Source = source.Path, Bytes = new FileInfo(source.Path).Length });
            }
            catch (IOException ex)
            {
                warnings.Add((JsonNode?)JsonValue.Create(ex.Message));
            }
        }

        var manifest = new DiagnosticBundleManifest
        {
            SessionId = sessionId,
            Files = inventory,
            Warnings = warnings,
            RedactionEnabled = redact,
            Redaction = "Launch arguments and environment overrides omitted. Optional best-effort token/password/key text redaction; private paths and arbitrary secrets may remain.",
            CreatedAt = DateTimeOffset.UtcNow.ToString("O")
        };
        if (destination is null)
            return manifest;
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                async Task Write(string name, JsonNode value)
                {
                    var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Fastest).Open());
                    await using var writerDisposal = writer.ConfigureAwait(false);
                    await writer.WriteAsync((redact ? Sanitize(value) : value).ToJsonString().AsMemory(), ct).ConfigureAwait(false);
                }

                await Write("session.json", state).ConfigureAwait(false);
                await Write("diagnosis.json", JsonSerializer.SerializeToNode(diagnosis, ApplicationJsonContext.Default.SessionDiagnosis)!).ConfigureAwait(false);
                await Write("launcher-activity.json", JsonSerializer.SerializeToNode(await new LauncherActivityLog(paths).ReadAsync(
                    session.InstanceId, session.InstanceId is null ? session.JobId : null, cancellationToken: ct).ConfigureAwait(false), ApplicationJsonContext.Default.IReadOnlyListLauncherActivityEntry)!).ConfigureAwait(false);
                foreach (var (key, snapshot) in snapshots)
                {
                    if (snapshot.Source == "unavailable") continue;
                    var output = zip.CreateEntry("logs/" + key + ".log", CompressionLevel.Fastest).Open();
                    await using var outputDisposal = output.ConfigureAwait(false);
                    if (!redact)
                    {
                        await output.WriteAsync(snapshot.Bytes, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        using var input = new MemoryStream(snapshot.Bytes, writable: false);
                        using var reader = new StreamReader(input, LogEncoding.Resolve(key == "stdoutLog" ? session.StdoutEncoding : session.StderrEncoding, out _));
                        var writer = new StreamWriter(output, leaveOpen: true);
                        await using var writerDisposal = writer.ConfigureAwait(false);
                        await RedactStreamAsync(reader, writer, ct).ConfigureAwait(false);
                    }
                }

                foreach (var source in sources)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var input = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        await using var inputDisposal = input.ConfigureAwait(false);
                        var output = zip.CreateEntry(source.Name, CompressionLevel.Fastest).Open();
                        await using var outputDisposal = output.ConfigureAwait(false);
                        if (!redact)
                        {
                            await input.CopyToAsync(output, ct).ConfigureAwait(false);
                        }
                        else
                        {
                            using var reader = new StreamReader(input, LogEncoding.Resolve(source.Encoding, out _));
                            var writer = new StreamWriter(output);
                            await using var writerDisposal = writer.ConfigureAwait(false);
                            await RedactStreamAsync(reader, writer, ct).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        warnings.Add((JsonNode)new JsonObject { ["path"] = source.Path, ["error"] = ex.Message });
                    }
                }

                await Write("manifest.json", JsonSerializer.SerializeToNode(manifest, ApplicationJsonContext.Default.DiagnosticBundleManifest)!).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            File.Move(temp, destination, false);
            manifest.Path = destination;
            return manifest;
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static string Redact(string text)
    {
        text = Regex.Replace(text, @"(?i)((?:access[_-]?token|refresh[_-]?token|api[_-]?key|password)\\?[""']?\s*[:=]\s*)([""'])(?:\\.|(?!\2|\\).)*\2", "$1$2[REDACTED]$2", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        text = Regex.Replace(text, @"(?i)(Bearer\s+)[A-Za-z0-9._~+/=-]+", "$1[REDACTED]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        return Regex.Replace(text, "(?i)((?:access[_-]?token|refresh[_-]?token|api[_-]?key|password)\\\\?\"?\\s*[:=]\\s*\\\\?\"?)[^\\s\",;]+", "$1[REDACTED]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    }

    private static JsonNode Sanitize(JsonNode node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return JsonValue.Create(Redact(text))!;
        var clone = node.DeepClone();
        if (clone is JsonObject obj)
        {
            foreach (var key in obj.Select(x => x.Key).ToArray())
            {
                if (obj[key] is { } child)
                    obj[key] = MyRegex().IsMatch(key) ? JsonValue.Create("[REDACTED]") : Sanitize(child);
            }
        }
        else if (clone is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is { } child)
                    array[index] = Sanitize(child);
            }
        }

        return clone;
    }

    private static async Task RedactStreamAsync(StreamReader reader, StreamWriter writer, CancellationToken ct)
    {
        var buffer = new char[16384];
        var carry = string.Empty;
        var continuingToken = false;
        var quote = '\0';
        var escaped = false;
        var prefix = new Regex(@"(?i)(Bearer\s+|(?:access[_-]?token|refresh[_-]?token|api[_-]?key|password)\\?[""']?\s*[:=]\s*\\?[""']?)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        bool Consume(string text, ref int cursor)
        {
            while (cursor < text.Length)
            {
                var value = text[cursor];
                if (quote != '\0')
                {
                    if (escaped)
                    {
                        escaped = false;
                        cursor++;
                        continue;
                    }

                    if (value == '\\')
                    {
                        escaped = true;
                        cursor++;
                        continue;
                    }

                    if (value == quote)
                    {
                        quote = '\0';
                        return false;
                    }
                }
                else if (char.IsWhiteSpace(value) || value is '"' or ',' or ';')
                {
                    return false;
                }

                cursor++;
            }

            return true;
        }

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            var text = carry + new string(buffer, 0, read);
            var cursor = 0;
            if (continuingToken)
            {
                continuingToken = Consume(text, ref cursor);
            }

            var cutoff = read == 0 ? text.Length : Math.Max(cursor, text.Length - 512);
            while (cursor < cutoff)
            {
                var match = prefix.Match(text, cursor);
                if (!match.Success || match.Index >= cutoff)
                {
                    await writer.WriteAsync(text.AsMemory(cursor, cutoff - cursor), ct).ConfigureAwait(false);
                    cursor = cutoff;
                    break;
                }

                await writer.WriteAsync(text.AsMemory(cursor, match.Index - cursor), ct).ConfigureAwait(false);
                await writer.WriteAsync((match.Value + "[REDACTED]").AsMemory(), ct).ConfigureAwait(false);
                cursor = match.Index + match.Length;
                quote = match.Value.EndsWith('"') ? '"' : match.Value.EndsWith('\'') ? '\'' : '\0';
                escaped = false;
                continuingToken = Consume(text, ref cursor);
            }

            carry = text[cursor..];
            if (read == 0)
                break;
        }
    }

    [GeneratedRegex(@"(?i)^(access[_-]?token|refresh[_-]?token|api[_-]?key|password)$", RegexOptions.CultureInvariant)]
    private static partial Regex MyRegex();
}