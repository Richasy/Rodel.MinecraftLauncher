// Copyright (c) Richasy. All rights reserved.

using System.Diagnostics;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Counts active model/tool execution while excluding human decisions and independent job waits, with caller cancellation always active.</summary>
internal sealed class AiTurnDeadline : IDisposable
{
    private readonly CancellationTokenSource _source;
    private readonly TimeSpan _limit;
    private readonly Stopwatch _active = Stopwatch.StartNew();
    private readonly HashSet<string> _waits = new(StringComparer.Ordinal);

    /// <summary>Starts one execution budget linked to the caller's stop and host-lifetime token.</summary>
    internal AiTurnDeadline(TimeSpan limit, CancellationToken cancellationToken)
    {
        _limit = limit;
        _source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _source.CancelAfter(limit);
    }

    /// <summary>Gets the cancellation token shared by the running model and tools.</summary>
    internal CancellationToken Token => _source.Token;
    /// <summary>Gets whether the execution budget or caller has canceled this turn.</summary>
    internal bool IsCancellationRequested => _source.IsCancellationRequested;

    /// <summary>Excludes an identified approval or question wait without disabling caller cancellation.</summary>
    internal void PauseForUser(string id) => Pause("user:" + id);

    /// <summary>Resumes the remaining active budget after an identified user wait ends.</summary>
    internal void ResumeAfterUser(string id) => Resume("user:" + id);

    /// <summary>Tracks actual approval and independent-job wait events emitted by the host executor.</summary>
    internal void ObserveWaitEvent(string kind, string? step)
    {
        if (step is null) return;
        switch (kind)
        {
            case "confirmation_required": PauseForUser(step); break;
            case "confirmation_finished": ResumeAfterUser(step); break;
            case "job_wait_started": Pause("job:" + step); break;
            case "job_wait_finished": Resume("job:" + step); break;
        }
    }

    private void Pause(string id)
    {
        if (!_waits.Add(id) || _waits.Count != 1) return;
        Token.ThrowIfCancellationRequested();
        _source.CancelAfter(Timeout.InfiniteTimeSpan);
        _active.Stop();
    }

    private void Resume(string id)
    {
        if (!_waits.Remove(id) || _waits.Count != 0) return;
        _active.Start();
        var remaining = _limit - _active.Elapsed;
        _source.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }

    /// <inheritdoc />
    public void Dispose() => _source.Dispose();
}