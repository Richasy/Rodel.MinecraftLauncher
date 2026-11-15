// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

internal sealed partial class AiServiceModelChoice(AiServiceDialog owner, AiUiModel initial) : Control
{
    private partial AiUiModel Model { get; set; }

    internal void Update(AiUiModel model) => Model = model;
    private AiUiModel Value => Model ?? initial;
    private bool Selected
    {
        get
        {
            _ = owner.CatalogRevision;
            return owner.SelectedModels.Contains(Value.Code);
        }
    }

    private partial bool Hovered { get; set; }

    private Ui Body => HoverArea(new AiModelCard(
LauncherAppearance.Capture(), Value.Name,
            Value.Code,
            Selected,
            Hovered,
            !owner.Working,
            () => owner.ToggleModel(Value.Code),
            "ai.service.select." + Value.Code), value => Hovered = value);
}