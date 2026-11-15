// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

internal sealed partial class AiDiscoveredModel(AiDiscoveryDialog owner, AiUiModel initial) : Control
{
    private partial AiUiModel Model { get; set; }

    internal void Update(AiUiModel model) => Model = model;
    private AiUiModel Value => Model ?? initial;
    private bool Selected
    {
        get
        {
            _ = owner.Revision;
            return owner.Selected.Contains(Value.Code);
        }
    }

    private partial bool Hovered { get; set; }

    private Ui Body => HoverArea(new AiModelCard(
LauncherAppearance.Capture(), Value.Name,
            Value.Code,
            Selected,
            Hovered,
            !owner.Working,
            () => owner.Toggle(Value.Code),
            "ai.discovered." + Value.Code), value => Hovered = value);
}