// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiCallGrant : IAsyncDisposable
{
    private readonly CancellationTokenSource _call;
    private readonly CancellationTokenSource _watch = new();
    private readonly Task _monitor;
    private int _stopped;
    public CancellationToken Token => _call.Token;
    public Exception? Revocation { get; private set; }

    public AiCallGrant(Func<CancellationToken, Task> check, CancellationToken ct)
    {
        _call = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _monitor = Task.Run(async () =>
        {
            try
            {
                while (!_watch.IsCancellationRequested)
                {
                    await Task.Delay(150, _watch.Token).ConfigureAwait(false);
                    try { await check(_watch.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (_watch.IsCancellationRequested) { return; }
                    catch (Exception error)
                    {
                        Revocation = error;
                        await _call.CancelAsync().ConfigureAwait(false);
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (_watch.IsCancellationRequested) { }
        }, CancellationToken.None);
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0) await _watch.CancelAsync().ConfigureAwait(false);
        await _monitor.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _watch.Dispose();
        _call.Dispose();
    }
}