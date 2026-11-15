// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>An observed startup marker; elapsed intervals include all concurrent work and are not component execution times.</summary>
/// <param name="Stage">Stable launcher, loader or game milestone identifier.</param>
/// <param name="Source">Launcher action, process window observation, stdout or stderr.</param>
/// <param name="ElapsedMilliseconds">Monotonic elapsed time since monitor preparation began.</param>
/// <param name="SincePreviousMilliseconds">Elapsed time since the previous observed marker.</param>
/// <param name="ProcessCpuMilliseconds">Total game-process CPU time across all threads, when available.</param>
public sealed record StartupMilestone(string Stage, string Source, long ElapsedMilliseconds,
    long SincePreviousMilliseconds, long? ProcessCpuMilliseconds);

/// <summary>Small startup evidence retained independently of the memory-only game console.</summary>
/// <param name="Status">Observation outcome; initialization observations do not establish menu or world readiness.</param>
/// <param name="ElapsedMilliseconds">Elapsed time covered by this snapshot.</param>
/// <param name="Milestones">First observations in monitor order, with a half-second sampling resolution for game events.</param>
/// <param name="ProcessCpuMilliseconds">Latest total game-process CPU time; it can exceed wall time on multiple cores.</param>
/// <param name="WorkingSetBytes">Latest observed resident memory, not the configured Java heap.</param>
/// <param name="OutputTruncated">Whether a console gap or oversized line limited marker observation.</param>
/// <param name="Warning">Diagnostic observation or persistence failure, if any.</param>
public sealed record StartupReport(string Status, long ElapsedMilliseconds, IReadOnlyList<StartupMilestone> Milestones,
    long? ProcessCpuMilliseconds, long? WorkingSetBytes, bool OutputTruncated, string? Warning)
{
    /// <summary>Gets the longest completed gap, named by the marker observed at its end; it does not identify a cause.</summary>
    public StartupMilestone? LongestObservedGap => Milestones.MaxBy(item => item.SincePreviousMilliseconds);

    /// <summary>Explains why marker gaps alone cannot identify a slow mod, network endpoint or CPU operation.</summary>
    public string Interpretation { get; init; } = "Elapsed intervals are gaps between observed markers, not component execution times. Missing markers do not prove a hang. CPU time includes all game threads; no culprit is inferred.";
}

/// <summary>Samples bounded, already-redacted console windows without delaying their producers or enabling full console persistence.</summary>
internal sealed class StartupDiagnostics : IAsyncDisposable
{
    private readonly string _file;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly List<StartupMilestone> _milestones = [];
    private readonly Task _worker;
    private readonly ConsoleReader _stdout;
    private readonly ConsoleReader _stderr;
    private Process? _process;
    private GameLogHost? _capture;
    private long? _cpu;
    private long? _memory;
    private string _status = "observing";
    private long? _finishedAt;
    private string? _warning;
    private bool _truncated;
    private long _lastSavedAt;
    private int _lastSavedCount = -1;
    private string? _lastSavedStatus;

    internal StartupDiagnostics(LauncherPaths paths, string session, string? stdoutEncoding, string? stderrEncoding)
    {
        _file = FilePath(paths, session);
        _stdout = new(LogEncoding.Resolve(stdoutEncoding, out _));
        _stderr = new(LogEncoding.Resolve(stderrEncoding, out _));
        Mark("preparation_started");
        _worker = Task.Run(ObserveAsync);
    }

    private static string FilePath(LauncherPaths paths, string session) => Path.Combine(paths.LogsDirectory, session + ".startup.json");

    internal static StartupReport? Read(LauncherPaths paths, string session)
    {
        try
        {
            var file = FilePath(paths, session);
            return File.Exists(file) ? JsonSerializer.Deserialize(File.ReadAllText(file), LaunchJsonContext.Default.StartupReport) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new("unavailable", 0, [], null, null, false, error.Message);
        }
    }

    internal void Mark(string stage, string source = "launcher")
    {
        lock (_gate)
        {
            if (_status != "observing" || _milestones.Any(item => item.Stage == stage)) return;
            var elapsed = _watch.ElapsedMilliseconds;
            _milestones.Add(new(stage, source, elapsed, elapsed - (_milestones.LastOrDefault()?.ElapsedMilliseconds ?? 0), _cpu));
        }
    }

    internal void Bind(Process process, GameLogHost capture)
    {
        lock (_gate)
        {
            _process = process; _capture = capture;
            Mark("process_started");
        }
    }

    internal void End(string status)
    {
        lock (_gate)
        {
            if (_status != "observing") return;
            _status = status;
            _finishedAt = _watch.ElapsedMilliseconds;
        }
    }

    internal void Complete()
    {
        Observe();
        End("process_exited");
    }

    private async Task ObserveAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            do
            {
                Observe();
                if (NeedsSave()) await SaveAsync().ConfigureAwait(false);
                lock (_gate)
                {
                    if (_status != "observing") return;
                    if (_watch.Elapsed >= TimeSpan.FromMinutes(3)) End("observation_timeout");
                }
            }
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            lock (_gate) { _warning = error.Message; End("observation_failed"); }
        }
    }

    private void Observe()
    {
        lock (_gate)
        {
            if (_status != "observing") return;
            if (_capture is not null)
            {
                _truncated |= _stdout.Read(_capture.Stdout, line => ObserveLine(line, "stdout"));
                _truncated |= _stderr.Read(_capture.Stderr, line => ObserveLine(line, "stderr"));
            }

            if (_process is null) return;
            try
            {
                _process.Refresh();
                _cpu = (long)_process.TotalProcessorTime.TotalMilliseconds;
                if (_process.HasExited) { End("process_exited"); return; }

                _memory = _process.WorkingSet64;
                if (!_milestones.Any(item => item.Stage == "window_visible"))
                {
                    if (GameWindow.IsVisible(_process, out var warning)) Mark("window_visible", "process_window");
                    if (warning is not null) _warning = warning;
                }

                if (_milestones.Any(item => item.Stage == "window_visible")
                    && _milestones.Any(item => item.Stage == "sound_ready")
                    && _milestones.Any(item => item.Stage == "textures_ready"))
                {
                    End("initialization_observed");
                }
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _warning = error.Message;
            }
        }
    }

    private void ObserveLine(string line, string source)
    {
        var stage = Classify(line);
        if (stage is not null) Mark(stage, source);
    }

    internal static string? Classify(string line) => line switch
    {
        _ when line.Contains("Loading Minecraft ", StringComparison.Ordinal) => "loader_started",
        _ when line.Contains("SpongePowered MIXIN Subsystem", StringComparison.Ordinal) => "mixins_started",
        _ when line.Contains("Datafixer optimizations took", StringComparison.Ordinal) => "datafixers_observed",
        _ when line.Contains("Setting user:", StringComparison.Ordinal) => "client_initialization",
        _ when line.Contains("io exception while checking patreon:", StringComparison.Ordinal)
            && line.Contains("Read timed out", StringComparison.Ordinal) => "xaero_patreon_read_timeout_observed",
        _ when line.Contains("Backend library: LWJGL", StringComparison.Ordinal) => "graphics_initialization",
        _ when line.Contains("Created window using", StringComparison.Ordinal) => "window_created_log",
        _ when line.Contains("Reloading ResourceManager:", StringComparison.Ordinal) => "resource_reload",
        _ when line.Contains("Sound engine started", StringComparison.Ordinal) => "sound_ready",
        _ when line.Contains("minecraft:textures/atlas/gui.png-atlas", StringComparison.Ordinal) => "textures_ready",
        _ when line.Contains("Rodel title menu initialized:", StringComparison.Ordinal) => "title_screen_observed",
        _ => null
    };

    private async Task SaveAsync()
    {
        StartupReport report;
        lock (_gate) report = new(_status, _finishedAt ?? _watch.ElapsedMilliseconds, [.. _milestones], _cpu, _memory, _truncated, _warning);
        try
        {
            await JsonFiles.WriteAsync(_file, JsonSerializer.SerializeToNode(report, LaunchJsonContext.Default.StartupReport)!, CancellationToken.None).ConfigureAwait(false);
            lock (_gate)
            {
                _lastSavedAt = report.ElapsedMilliseconds;
                _lastSavedCount = report.Milestones.Count;
                _lastSavedStatus = report.Status;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            lock (_gate) _warning = error.Message;
            Trace.TraceWarning("Startup diagnostics could not be saved: {0}", error.Message);
        }
    }

    private bool NeedsSave()
    {
        lock (_gate) return _milestones.Count != _lastSavedCount || _status != _lastSavedStatus || _watch.ElapsedMilliseconds - _lastSavedAt >= 5000;
    }

    /// <summary>Stops the bounded observer and publishes its final snapshot; does not stop the game or persist console content.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _worker.ConfigureAwait(false);
        Observe();
        End("monitor_stopped");

        await SaveAsync().ConfigureAwait(false);
        _stop.Dispose();
    }

    private sealed class ConsoleReader(Encoding encoding)
    {
        private readonly Decoder _decoder = encoding.GetDecoder();
        private readonly StringBuilder _line = new();
        private long _offset;
        private bool _skipLine;

        internal bool Read(GameLogBuffer buffer, Action<string> observe)
        {
            var snapshot = buffer.Read(_offset, 65536);
            var truncated = snapshot.Offset != _offset;
            if (truncated) { _decoder.Reset(); _line.Clear(); _skipLine = snapshot.LeadingPartialLine; }

            var final = !snapshot.Capture!.Capturing && snapshot.Offset + snapshot.Bytes.Length == snapshot.TotalBytes;
            var chars = new char[encoding.GetMaxCharCount(snapshot.Bytes.Length)];
            var count = _decoder.GetChars(snapshot.Bytes, chars, flush: final);
            foreach (var ch in chars.AsSpan(0, count))
            {
                if (ch == '\n')
                {
                    if (!_skipLine) observe(_line.ToString());
                    _line.Clear(); _skipLine = false;
                }
                else if (!_skipLine && _line.Length < 16384)
                {
                    _line.Append(ch);
                }
                else if (!_skipLine) { _line.Clear(); _skipLine = true; truncated = true; }
            }

            if (final && !_skipLine && _line.Length > 0) { observe(_line.ToString()); _line.Clear(); }

            _offset = snapshot.Offset + snapshot.Bytes.Length;
            return truncated;
        }
    }
}