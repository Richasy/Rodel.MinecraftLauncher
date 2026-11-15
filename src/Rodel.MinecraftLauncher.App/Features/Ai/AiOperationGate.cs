// Copyright (c) Richasy. All rights reserved.

using Sprout.Reactive;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Preserves the window's existing short-request exclusion across configuration and chat owners.</summary>
internal sealed class AiOperationGate
{
    private readonly Signal<bool> _busy = new(false);
    private TaskCompletionSource? _released;
    internal bool Busy => _busy.Value;
    internal Task WhenAvailable => _released?.Task ?? Task.CompletedTask;

    internal bool TryEnter()
    {
        if (Busy)
            return false;
        _released = new();
        _busy.Value = true;
        return true;
    }

    internal void Exit()
    {
        var released = _released;
        _released = null;
        _busy.Value = false;
        released?.TrySetResult();
    }
}