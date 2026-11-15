// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Bounded launcher workflow evidence. Payloads deliberately exclude paths, names, URLs, arguments and exception messages.</summary>
public sealed class LauncherActivityLog(LauncherPaths paths)
{
    private static readonly AsyncLocal<string?> JobScope = new();
    private static readonly AsyncLocal<string?> InstanceScope = new();
    private const long SegmentBytes = 8 * 1024 * 1024;
    private string DirectoryPath => Path.Combine(paths.LogsDirectory, "launcher");

    internal static IDisposable Scope(string jobId, string? instanceId)
    {
        var previous = JobScope.Value;
        var previousInstance = InstanceScope.Value;
        JobScope.Value = jobId;
        InstanceScope.Value = instanceId;
        return new ScopeReset(() => { JobScope.Value = previous; InstanceScope.Value = previousInstance; });
    }

    /// <summary>Records safe workflow evidence and optional numeric metrics. Returns false when logging is unavailable.</summary>
    public async Task<bool> WriteAsync(string eventName, string? instanceId = null, string? jobId = null,
        string? stage = null, string? code = null, int? count = null, long? bytes = null, int? nativeError = null, int? httpStatus = null,
        IReadOnlyDictionary<string, long>? metrics = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMilliseconds(200));
            using var lease = await ResourceLease.AcquireAsync(resource: DirectoryPath, cancellationToken: deadline.Token).ConfigureAwait(false);
            Directory.CreateDirectory(DirectoryPath);
            var today = DateTime.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            var file = Path.Combine(DirectoryPath, "launcher-" + today + ".jsonl");
            if (File.Exists(file) && new FileInfo(file).Length >= SegmentBytes)
            {
                File.Delete(file + ".3");
                for (var segment = 2; segment >= 1; segment--)
                    if (File.Exists(file + "." + segment)) File.Move(file + "." + segment, file + "." + (segment + 1), true);
                File.Move(file, file + ".1", true);
            }

            var cutoff = DateTime.UtcNow.Date.AddDays(-6);
            foreach (var old in Directory.EnumerateFiles(DirectoryPath, "launcher-*.jsonl*"))
            {
                var name = Path.GetFileName(old);
                if (name.Length >= 17 && DateTime.TryParseExact(name.AsSpan(9, 8), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var day) && day < cutoff)
                {
                    File.Delete(old);
                }
            }

            var entry = new JsonObject
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("O"),
                ["event"] = Safe(eventName),
                ["level"] = eventName.Contains("failed", StringComparison.Ordinal) ? "error" : eventName.Contains("fallback", StringComparison.Ordinal) || eventName.Contains("advisory", StringComparison.Ordinal) ? "warning" : "information",
                ["instanceId"] = Identifier(instanceId ?? InstanceScope.Value),
                ["jobId"] = Identifier(jobId ?? JobScope.Value),
                ["stage"] = Safe(stage),
                ["code"] = Safe(code),
                ["count"] = count,
                ["bytes"] = bytes,
                ["nativeError"] = nativeError,
                ["httpStatus"] = httpStatus
            };
            if (metrics is not null)
            {
                var values = new JsonObject();
                foreach (var (key, value) in metrics)
                    if (Safe(key) is { } safeKey) values[safeKey] = value;
                entry["metrics"] = values;
            }

            await File.AppendAllTextAsync(file, entry.ToJsonString() + "\n", new UTF8Encoding(false), deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Reads recent structured evidence without including game output or private configuration.</summary>
    public async Task<IReadOnlyList<LauncherActivityEntry>> ReadAsync(string? instanceId = null, string? jobId = null, int limit = 200, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        if (!Directory.Exists(DirectoryPath)) return [];
        var entries = new List<JsonObject>();
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "launcher-*.jsonl*").OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var recent = new Queue<JsonObject>();
                while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    JsonObject? entry;
                    try { entry = JsonNode.Parse(line) as JsonObject; }
                    catch (System.Text.Json.JsonException) { continue; } // A concurrent writer may not have completed its last line.

                    if (entry is null || (instanceId is not null && entry["instanceId"]?.ToString() != instanceId)
                        || (jobId is not null && entry["jobId"]?.ToString() != jobId))
                    {
                        continue;
                    }

                    recent.Enqueue(entry);
                    if (recent.Count > limit) recent.Dequeue();
                }

                entries.AddRange(recent);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }

        return [.. entries.OrderBy(e => e["time"]?.ToString(), StringComparer.Ordinal).TakeLast(limit).Select(LauncherActivityEntry.Read)];
    }

    private static string? Safe(string? value) => value is not null && value.Length <= 80 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') ? value : null;
    private static string? Identifier(string? value) => Guid.TryParseExact(value, "N", out _) ? value : null;
    private sealed class ScopeReset(Action reset) : IDisposable { public void Dispose() => reset(); }
}