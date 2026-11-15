// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>A byte-bounded console tail; producer work never waits for disk or a pipe client.</summary>
internal sealed class GameLogBuffer
{
    internal const int Capacity = 1024 * 1024;
    private readonly byte[] _bytes = new byte[Capacity];
    private readonly Lock _gate = new();
    private long _total;
    private bool _capturing = true;
    private string? _warning;

    internal async Task CaptureAsync(Stream input)
    {
        var buffer = new byte[8192];
        try
        {
            while (await input.ReadAsync(buffer).ConfigureAwait(false) is var read && read != 0)
            {
                lock (_gate)
                {
                    var position = (int)(_total % Capacity);
                    var first = Math.Min(read, Capacity - position);
                    buffer.AsSpan(0, first).CopyTo(_bytes.AsSpan(position));
                    buffer.AsSpan(first, read - first).CopyTo(_bytes);
                    _total += read;
                }
            }
        }
        catch (IOException error)
        {
            lock (_gate) _warning = "Game output could not be read completely: " + error.Message;
        }
        finally
        {
            lock (_gate) _capturing = false;
        }
    }

    internal LogReadSnapshot Read(long? offset, int budget)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset ?? 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(budget, Capacity);
        lock (_gate)
        {
            var first = Math.Max(0, _total - Capacity);
            if (offset > _total) throw new ArgumentOutOfRangeException(nameof(offset), "Log cursor is beyond the captured stream.");
            var start = Math.Max(first, offset ?? Math.Max(0, _total - budget));
            var bytes = new byte[(int)Math.Min(budget, _total - start)];
            var position = (int)(start % Capacity);
            var count = Math.Min(bytes.Length, Capacity - position);
            _bytes.AsSpan(position, count).CopyTo(bytes);
            _bytes.AsSpan(0, bytes.Length - count).CopyTo(bytes.AsSpan(count));
            var partial = start > 0 && (start == first || _bytes[(int)((start - 1) % Capacity)] != (byte)'\n');
            var status = new LogCaptureStatus(!_capturing && first == 0 && _warning is null, _capturing,
                _total, first, _warning, _total - first);
            return new(bytes, start, first, _total, partial, status, "memory", _warning);
        }
    }
}