// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Downloads;

/// <summary>Reports completed entries in one known batch while retaining current-file transfer details.</summary>
internal sealed class DownloadBatchProgress(IProgress<DownloadProgress>? target, int total) : IProgress<DownloadProgress>
{
    private readonly Lock _gate = new();
    private int _completed;

    /// <inheritdoc/>
    public void Report(DownloadProgress value)
    {
        lock (_gate)
            target?.Report(value with { OverallPercent = total > 0 ? _completed * 100d / total : 0d });
    }

    internal void Complete(string path)
    {
        lock (_gate)
        {
            _completed++;
            Report(new(path, 0, null, false));
        }
    }
}