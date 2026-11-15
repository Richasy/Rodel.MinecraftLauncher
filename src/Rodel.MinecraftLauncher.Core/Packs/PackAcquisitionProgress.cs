// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Downloads;

namespace Rodel.MinecraftLauncher.Core.Packs;

/// <summary>Aggregates bounded concurrent file work by declared bytes, falling back to file fractions when sizes are unknown.</summary>
internal sealed class PackAcquisitionProgress
{
    private readonly IProgress<DownloadProgress>? _target;
    private readonly Lock _gate = new();
    private readonly double[] _weights;
    private readonly double[] _completed;
    private readonly double _total;
    private double _done;

    internal PackAcquisitionProgress(IProgress<DownloadProgress>? target, IReadOnlyList<long?> sizes)
    {
        _target = target;
        var known = sizes.All(size => size is >= 0) && sizes.Any(size => size is > 0);
        _weights = [.. sizes.Select(size => known ? size!.Value : 1d)];
        _completed = new double[sizes.Count];
        _total = _weights.Sum();
    }

    internal FileProgress ForFile(int index) => new(this, index);

    private void Report(int index, DownloadProgress value, bool complete)
    {
        if (_target is null) return;
        lock (_gate)
        {
            var fraction = complete ? 1d : value.TotalBytes is > 0 ? Math.Clamp(value.Bytes / (double)value.TotalBytes, 0d, 1d) : 0d;
            var next = Math.Max(_completed[index], fraction * _weights[index]);
            _done += next - _completed[index];
            _completed[index] = next;
            _target.Report(value with { OverallPercent = _total > 0 ? Math.Clamp(_done * 100d / _total, 0d, 100d) : 100d });
        }
    }

    internal sealed class FileProgress(PackAcquisitionProgress owner, int index) : IProgress<DownloadProgress>
    {
        /// <inheritdoc/>
        public void Report(DownloadProgress value) => owner.Report(index, value, false);

        internal void Complete(string path) => owner.Report(index, new(path, 0, null, false), true);
    }
}