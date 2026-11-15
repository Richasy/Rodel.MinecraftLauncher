// Copyright (c) Richasy. All rights reserved.

using Sprout.Styling;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed record HomeInstanceRow(
    InstanceSummary Instance,
    bool Selected,
    bool Last,
    ButtonStyle Appearance);