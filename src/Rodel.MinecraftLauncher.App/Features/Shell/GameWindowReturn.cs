// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;

namespace Rodel.MinecraftLauncher.App.Features.Shell;

/// <summary>Consumes one automatic window-return intent after every observed game and pending launch has ended.</summary>
internal sealed class GameWindowReturn
{
    private bool _pending;

    internal void Arm() => _pending = true;

    internal void Cancel() => _pending = false;

    internal bool Observe(HomePageState state, bool preparingLaunch)
    {
        if (!_pending || preparingLaunch || !state.LaunchObserved || state.LaunchReadError.Length > 0
            || state.LaunchStates.Values.Any(launch => launch.Busy))
        {
            return false;
        }

        _pending = false;
        return true;
    }
}