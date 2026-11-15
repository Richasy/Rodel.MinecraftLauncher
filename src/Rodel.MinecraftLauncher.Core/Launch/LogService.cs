// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Reads live bounded console windows or crash snapshots without persisting normal game output.</summary>
public sealed class LogService(LauncherPaths paths)
{
    /// <summary>Reads independent absolute byte cursors, reporting evicted prefixes and unavailable memory explicitly.</summary>
    public async Task<LogPreview> ReadAsync(string sessionId, int tail = 100, int byteBudget = 65536,
        long? stdoutOffset = null, long? stderrOffset = null, CancellationToken ct = default)
    {
        if (byteBudget is < 8 or > 1048576)
            throw new ArgumentOutOfRangeException(nameof(byteBudget), "Log preview budget must be between 8 and 1048576 bytes.");
        ArgumentOutOfRangeException.ThrowIfLessThan(tail, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(stdoutOffset ?? 0);
        ArgumentOutOfRangeException.ThrowIfNegative(stderrOffset ?? 0);
        var cleanupWarnings = GameLogRetention.PruneIfDue(paths);
        var state = new SessionService(paths).Get(sessionId);
        var streams = new Dictionary<string, LogStreamSummary>(StringComparer.Ordinal);
        var output = new List<string>();
        var errors = new List<string>();
        foreach (var stderr in new[] { false, true })
        {
            var offset = stderr ? stderrOffset : stdoutOffset;
            var snapshot = await ReadBytesAsync(state, stderr, offset, byteBudget / 2, ct).ConfigureAwait(false);
            var encoding = LogEncoding.Resolve(stderr ? state.StderrEncoding : state.StdoutEncoding, out var encodingWarning);
            var bytes = snapshot.Bytes;
            var leading = 0;
            while (encoding.CodePage == 65001 && leading < bytes.Length && (bytes[leading] & 0xC0) == 0x80) leading++;
            var (text, consumed) = LogEncoding.Decode(bytes, leading, bytes.Length - leading, encoding,
                snapshot.Capture?.Capturing != true && snapshot.Offset + bytes.Length == snapshot.TotalBytes);
            var pieces = text.Split('\n');
            var count = pieces.Length;
            if (count > 0 && pieces[^1].Length == 0) count--;
            var first = offset is null ? Math.Max(0, count - tail) : 0;
            var lines = stderr ? errors : output;
            for (var i = first; i < count; i++) lines.Add(pieces[i].TrimEnd('\r'));
            streams[stderr ? "stderrLog" : "stdoutLog"] = new()
            {
                Source = snapshot.Source,
                Warning = string.Join("\n", cleanupWarnings.Concat(snapshot.Warning is null ? [] : new[] { snapshot.Warning })) is { Length: > 0 } warning ? warning : null,
                FirstOffset = snapshot.FirstOffset,
                Encoding = encoding.WebName,
                EncodingWarning = encodingWarning,
                Available = snapshot.Source != "unavailable",
                Offset = snapshot.Offset + leading,
                NextOffset = snapshot.Offset + leading + consumed,
                RetainedBytes = snapshot.TotalBytes - snapshot.FirstOffset,
                ReturnedBytes = consumed,
                Truncated = snapshot.FirstOffset > 0 || snapshot.Offset > 0 || snapshot.Offset + leading + consumed < snapshot.TotalBytes || first > 0,
                LeadingPartialLine = first == 0 && (snapshot.LeadingPartialLine || leading > 0),
                TrailingPartialLine = text.Length > 0 && !text.EndsWith('\n'),
                HasMore = snapshot.Offset + leading + consumed < snapshot.TotalBytes,
                SegmentCount = snapshot.Source == "saved" ? 1 : 0,
                Capture = snapshot.Capture is null ? null : JsonSerializer.SerializeToNode(snapshot.Capture, LaunchJsonContext.Default.LogCaptureStatus)
            };
        }

        return new(sessionId, byteBudget, streams, output, errors) { Startup = state.Startup };
    }

    internal async Task<LogReadSnapshot> ReadBytesAsync(SessionStatus state, bool stderr, long? offset, int budget, CancellationToken ct)
    {
        var file = stderr ? state.StderrLog : state.StdoutLog;
        if (file is not null && File.Exists(file))
        {
            var status = (stderr ? state.StderrLogStatus : state.StdoutLogStatus)?.Deserialize(LaunchJsonContext.Default.LogCaptureStatus);
            var first = status?.DiscardedBytes ?? 0;
            var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 8192, true);
            await using var disposal = input.ConfigureAwait(false);
            var total = first + input.Length;
            if (offset > total) throw new ArgumentOutOfRangeException(nameof(offset), "Log cursor is beyond the captured stream.");
            var start = Math.Max(first, offset ?? Math.Max(first, total - budget));
            input.Position = Math.Max(0, start - first - 1);
            var partial = start > 0 && (start == first || input.ReadByte() != (byte)'\n');
            input.Position = start - first;
            var bytes = new byte[(int)Math.Min(budget, total - start)];
            await input.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
            return new(bytes, start, first, total, partial, status, "saved", status?.Warning);
        }

        if (state.LogPipe is { } pipe && state.Status is "preparing" or "running")
        {
            try { return await GameLogHost.ReadAsync(pipe, new(stderr, offset, budget), ct).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException
                or UnauthorizedAccessException or JsonException)
            {
                ct.ThrowIfCancellationRequested();
                var current = new SessionService(paths).Get(state.Id!);
                if (current.Status is not ("preparing" or "running"))
                    return await ReadBytesAsync(current, stderr, offset, budget, ct).ConfigureAwait(false);
                return new([], 0, 0, 0, false, null, "unavailable", "Live console read failed: " + error.Message);
            }
        }

        return new([], 0, 0, 0, false, null, "unavailable",
            (stderr ? state.StderrLogWarning : state.StdoutLogWarning)
            ?? (file is not null ? "The saved console snapshot is no longer available; launcher captures are retained for seven days."
                : "Console output was kept only in memory and is not available after the monitor exits."));
    }

    internal async Task<IReadOnlyDictionary<string, LogReadSnapshot>> SnapshotAsync(string sessionId, CancellationToken ct)
    {
        GameLogRetention.PruneIfDue(paths);
        var state = new SessionService(paths).Get(sessionId);
        return new Dictionary<string, LogReadSnapshot>
        {
            ["stdoutLog"] = await ReadBytesAsync(state, false, 0, GameLogBuffer.Capacity, ct).ConfigureAwait(false),
            ["stderrLog"] = await ReadBytesAsync(state, true, 0, GameLogBuffer.Capacity, ct).ConfigureAwait(false)
        };
    }
}