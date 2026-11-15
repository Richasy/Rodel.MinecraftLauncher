// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Features.Home;

/// <summary>Localized projections are captured by the owner, preserving previous values during reconciliation.</summary>
internal sealed record HomePageText(
    string Heading,
    string Runtime,
    string Configuration,
    string LaunchAction,
    string LaunchTitle,
    string LaunchDetail,
    string EmptyText,
    string LaunchBlockReason)
{
    internal static HomePageText Capture(HomePageState state) => new(
        state.Heading,
        state.Runtime,
        state.Configuration,
        state.LaunchAction,
        state.LaunchTitle,
        state.LaunchDetail,
        state.EmptyText,
        state.Details?.LaunchBlockReason ?? string.Empty);
}