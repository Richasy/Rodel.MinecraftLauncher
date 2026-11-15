// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Controls;

/// <summary>Shows an indeterminate read in the list viewport without presenting an empty inventory.</summary>
[View]
internal readonly partial record struct LoadingPlaceholderView(LauncherAppearance Theme, string Label, string AutomationId)
{
    /// <summary>Builds a centered activity indicator with a localized, politely announced status.</summary>
    public Ui Body => Row([
        ProgressRing(new Size(20f, 20f))
            .Style(default(ProgressRingStyle).Foreground(Theme.ProgressAccent).Thickness(2f))
            .VAlign(VerticalAlignment.Center),
        Text(Label)
            .FontSize(14f).Foreground(Theme.Ink)
            .Wrap().Shrink(1f).MinContentWidth(0f)
            .VAlign(VerticalAlignment.Center)
            .Automation(new()
            {
                Name = Label,
                AutomationId = AutomationId,
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
    ])
        .Spacing(12f)
        .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center);
}