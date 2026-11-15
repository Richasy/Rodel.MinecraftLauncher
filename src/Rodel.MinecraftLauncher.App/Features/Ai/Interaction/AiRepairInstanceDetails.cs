// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Shows the repair's bound instance without offering a different context or changing launcher selection.</summary>
internal sealed partial class AiRepairInstanceDetails(InstanceSummary? instance, string name) : Control
{
    private Ui Body => Column([
        Text(Resources.AiRepairInstanceTitle)
            .FontSize(11f).Foreground(LauncherTheme.Muted),
        SelectableText(Text(name)
                .FontSize(14f).Foreground(LauncherTheme.Ink)
                .Wrap())
            .Automation(new()
            {
                Name = name,
                AutomationId = "ai.repair.instance.name"
            }),
        Grid(
            [GridLength.Auto, GridLength.Star()],
            [GridLength.Auto, GridLength.Auto],
            [
                Text(Resources.RuntimeMinecraft)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Cell(0, 0),
                SelectableText(Text(instance?.DisplayVersion ?? Resources.ValueNotAvailable)
                        .FontSize(12f).Foreground(LauncherTheme.Ink)
                        .Wrap())
                    .Automation(new() { AutomationId = "ai.repair.instance.version" })
                    .Cell(1, 0),
                Text(Resources.RuntimeLoader)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Cell(0, 1),
                SelectableText(Text(instance?.Loader is "vanilla" or "none" ? Resources.Vanilla : instance?.Loader ?? Resources.ValueNotAvailable)
                        .FontSize(12f).Foreground(LauncherTheme.Ink)
                        .Wrap())
                    .Automation(new() { AutomationId = "ai.repair.instance.loader" })
                    .Cell(1, 1),
            ])
            .ColumnSpacing(16f).RowSpacing(8f),
    ])
        .Spacing(10f)
        .Padding(16f);
}