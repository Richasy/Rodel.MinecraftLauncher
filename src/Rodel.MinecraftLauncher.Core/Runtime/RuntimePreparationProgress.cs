// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Installation;
using Rodel.MinecraftLauncher.Core.Loaders;

namespace Rodel.MinecraftLauncher.Core.Runtime;

/// <summary>Keeps base-game and declared-loader progress within their ordered shares of one runtime preparation.</summary>
internal sealed class RuntimePreparationProgress(IProgress<InstallationProgress>? installation, IProgress<LoaderProgress>? loader, int phases) : IProgress<InstallationProgress>, IProgress<LoaderProgress>
{
    private int _completed;

    internal void CompletePhase() => _completed++;

    /// <inheritdoc/>
    public void Report(InstallationProgress value)
    {
        var percent = value.OverallPercent ?? (value.TotalFiles is > 0 and var total ? value.CompletedFiles * 100d / total : 0d);
        installation?.Report(value with { OverallPercent = Overall(percent) });
    }

    /// <inheritdoc/>
    public void Report(LoaderProgress value) => loader?.Report(value with { OverallPercent = Overall(value.OverallPercent ?? 0d) });

    private double Overall(double percent) => (_completed + Math.Clamp(percent / 100d, 0d, 1d)) * 100d / phases;
}