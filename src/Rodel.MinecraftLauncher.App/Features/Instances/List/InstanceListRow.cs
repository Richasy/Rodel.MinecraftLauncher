// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

internal sealed record InstanceListRowState(
    ManagedInstance Item,
    bool Compact,
    InstanceListAppearance Appearance,
    bool Selected,
    HomeLaunchState? Launch,
    bool Pending,
    string Runtime,
    string Configuration,
    string Content,
    string LaunchBlockReason,
    bool InstallationIncomplete)
{
    internal string Status => Pending
        ? Resources.LaunchUpdating
        : Launch?.Busy == true
        ? HomeLaunchPresentation.Title(Launch)
        : InstallationIncomplete
        ? Resources.InstanceInstallationIncomplete
        : LaunchBlockReason.Length > 0
        ? Item.Details?.LaunchIssue == "version" ? Resources.VersionNotConfigured : Resources.LaunchVersionFilesUnavailable
        : Launch is { Status: not ("idle" or "exited") } launch
        ? HomeLaunchPresentation.Title(launch)
        : Item.Error is not null ? Resources.MetadataUnreadable : Resources.InstanceIdle;
}

[View]
internal readonly partial record struct InstanceListRow(LauncherAppearance Theme, InstanceListRowState Data, InstancesPage Page)
{
    internal void OpenContent() => Page.Open(Data.Item.Id, InstanceAction.Content);
    internal string Status => Data.Status;

    public Ui Body => Border(Overlay([
            Grid(
                InstancesPage.Columns(Data.Compact),
                [GridLength.Star()],
                [
                    new InstanceNameCell(Data).Cell(0, 0),
                    new InstanceRuntimeCell(Theme,Data, Page).Cell(1, 0),
                    Button(Text(Data.Content)
                            .FontSize(12f)
                            .Wrap(), OpenContent)
                        .Style(Data.Appearance.Content)
                        .HAlign(HorizontalAlignment.Left)
                        .Automation(new()
                        {
                            Name = string.Format(Resources.InstanceManageAction, Resources.InstanceContentDetails, Data.Item.Name),
                            AutomationId = "instances.content." + Data.Item.Id
                        })
                        .VAlign(VerticalAlignment.Center)
                        .Visible(!Data.Compact)
                        .Cell(2, 0),
                    Text(Status, foreground: Data.Appearance.HighContrast ? (Color? )null : Data.Appearance.Muted)
                        .FontSize(11f)
                        .Wrap()
                        .VAlign(VerticalAlignment.Center)
                        .Visible(!Data.Compact)
                        .Automation(new()
                        {
                            Name = Status,
                            AutomationId = "instances.status." + Data.Item.Id
                        })
                        .Cell(Data.Compact ? 1 : 3, 0),
                    new InstanceActionsCell(Theme,Data, Page).Cell(Data.Compact ? 2 : 4, 0),
                ])
                .ColumnSpacing(16f)
                .Padding(Data.Compact ? 16f : 20f, 12f),
            Border()
                .Background(Data.Appearance.Indicator)
                .Width(3f).Height(24f)
                .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Center)
                .Visible(Data.Selected),
        ]))
        .MinHeight(Data.Compact ? 78f : 76f)
        .BorderBrush(Data.Appearance.Edge)
        .BorderThickness(0f, 0f, 0f, 1f);
}