// Copyright (c) Richasy. All rights reserved.

using System.Collections;

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Requests cancellation between two input actions without timers, scheduling assumptions or production hooks.</summary>
internal sealed class CancelBetweenEdits(SkinEdit first, SkinEdit second, CancellationTokenSource cancellation) : IReadOnlyList<SkinEdit>
{
    internal bool BoundaryReached { get; private set; }

    /// <inheritdoc/>
    public int Count => 2;

    /// <inheritdoc/>
    public SkinEdit this[int index] => index switch { 0 => first, 1 => second, _ => throw new ArgumentOutOfRangeException(nameof(index)) };

    /// <summary>Yields the first action, then cancels before supplying the second action.</summary>
    /// <returns>An enumerator that establishes the actual between-action cancellation boundary.</returns>
    public IEnumerator<SkinEdit> GetEnumerator()
    {
        yield return first;
        BoundaryReached = true;
        cancellation.Cancel();
        yield return second;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}