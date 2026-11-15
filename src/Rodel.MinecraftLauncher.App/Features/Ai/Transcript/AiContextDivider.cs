// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Marks a persisted context boundary without presenting it as model output.</summary>
internal sealed partial class AiContextDivider(string id, string initial) : Control
{
    private partial string Label { get; set; }

    internal void Update(string label) => Label = label;

    /// <summary>Builds two rules around a small, centered context label.</summary>
    private Ui Body => Grid(
        [GridLength.Star(), GridLength.Auto, GridLength.Star()],
        [GridLength.Auto],
        [
            Border()
                .Height(1f)
                .Background(LauncherTheme.Edge)
                .VAlign(VerticalAlignment.Center)
                .Cell(0, 0),
            Text(Label ?? initial)
                .Automation(new()
                {
                    Name = Label ?? initial,
                    AutomationId = "ai.context.change." + id
                })
                .FontSize(10f).Foreground(LauncherTheme.Quiet)
                .Wrap()
                .Cell(1, 0),
            Border()
                .Height(1f)
                .Background(LauncherTheme.Edge)
                .VAlign(VerticalAlignment.Center)
                .Cell(2, 0),
        ])
        .ColumnSpacing(12f);
}