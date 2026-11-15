// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Reconcile;

namespace Rodel.MinecraftLauncher.App.Features.Import;

[View]
internal readonly partial record struct ImportChoice(
    LauncherAppearance Theme,
    string Label,
    string Id,
    bool Selected,
    bool Enabled,
    Action Invoke)
{
    public Ui Body => LauncherControls.Command(Theme, Label, Invoke)
        .Style(Theme.Buttons.Navigation(Selected))
        .AsSelected(Selected)
        .Enabled(Enabled)
        .Automation(new()
        {
            Name = Label,
            AutomationId = Id
        });
}