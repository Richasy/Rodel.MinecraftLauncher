// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Controls.Buttons;

[View]
internal readonly partial record struct LauncherSplitButtonLabel(string Label, TextStyle Appearance)
{
    public Ui Body => Overlay([
        Text(Label)
            .Style(Appearance)
            .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center),
    ]);
}