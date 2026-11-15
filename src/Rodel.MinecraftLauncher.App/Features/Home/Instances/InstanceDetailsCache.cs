// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Features.Home;

/// <summary>Shares instance detail reads across the window without letting one surface cancel another.</summary>
internal sealed class InstanceDetailsCache(Func<string, CancellationToken, Task<HomeInstanceDetails>> read) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, Task<HomeInstanceDetails>> _reads = [with(StringComparer.Ordinal)];

    internal Task<HomeInstanceDetails> ReadAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<HomeInstanceDetails> pending;
        lock (_reads)
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            if (!_reads.TryGetValue(id, out pending!) || pending.IsFaulted || pending.IsCanceled)
            {
                var lifetime = _lifetime.Token;
                pending = Task.Run(() => read(id, lifetime), lifetime);
                _reads[id] = pending;
            }
        }

        return pending.WaitAsync(cancellationToken);
    }

    internal void Invalidate()
    {
        lock (_reads)
            _reads.Clear();
    }

    /// <summary>Cancels owned reads when the window closes; individual waiting surfaces do not own those reads.</summary>
    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        Invalidate();
    }
}