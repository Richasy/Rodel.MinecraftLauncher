// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

[View]
internal readonly partial record struct ContentFindingView(LauncherAppearance Theme, ContentFinding Data, string AutomationPrefix)
{
    /// <summary>Builds one wrapping notice with severity and a stable automation identity.</summary>
    public Ui Body => Text(Data.Summary)
        .FontSize(12f).Foreground(Data.Warning ? Theme.Warning : Theme.Error)
        .Wrap()
        .Automation(new()
        {
            Name = Data.Summary,
            AutomationId = AutomationPrefix + Data.Key
        });
}