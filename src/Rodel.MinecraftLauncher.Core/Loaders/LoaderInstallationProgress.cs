// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Installation;

namespace Rodel.MinecraftLauncher.Core.Loaders;

/// <summary>Counts actual base, metadata, library, optional processor and publication phases without estimating their duration.</summary>
internal sealed class LoaderInstallationProgress(IProgress<LoaderProgress>? target, int phases) : IProgress<LoaderProgress>, IProgress<InstallationProgress>
{
    private int _completed;
    private double _fraction;

    internal void CompletePhase(string stage, string message)
    {
        _completed++;
        _fraction = 0;
        Report(new LoaderProgress(stage, message));
    }

    /// <inheritdoc/>
    public void Report(LoaderProgress value)
    {
        if (value.Stage is "processor" or "processor-reused" && value.ProcessorCount is > 0 and var count && value.ProcessorIndex is { } index)
            _fraction = Math.Clamp((value.Stage == "processor-reused" ? index : index - 1d) / count, 0d, 1d);
        target?.Report(value with { OverallPercent = value.Stage == "completed" ? 100d : (_completed + _fraction) * 100d / phases });
    }

    /// <inheritdoc/>
    public void Report(InstallationProgress value)
    {
        _fraction = value.TotalFiles is > 0 and var total ? Math.Clamp(value.CompletedFiles / (double)total, 0d, 1d) : 0d;
        target?.Report(new(value.Stage, value.DisplayName ?? Path.GetFileName(value.Path)) { OverallPercent = _fraction * 100d / phases });
    }
}