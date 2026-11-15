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

[View]
internal readonly partial record struct InstanceNameCell(InstanceListRowState Data)
{
    internal string Status => Data.Status;

    public Ui Body => Row([
        new InstanceIconView(Data.Item.Instance.Describe()
                .Icon?["data"]?.ToString(), 30f, 34f).VAlign(VerticalAlignment.Center),
        Column([
            Text(Data.Item.Name, foreground: Data.Appearance.HighContrast ? (Color? )null : Data.Appearance.Ink)
                .FontSize(14f).FontWeight(600)
                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
            Text(Data.Item.Instance.ServerId is not null
                    ? Resources.ServerDedicatedClient
                    : Data.Selected ? Resources.CurrentInstance : Resources.InstanceLocal, foreground: Data.Appearance.HighContrast ? (Color? )null : Data.Appearance.Muted)
                .FontSize(11f)
                .Visible(!Data.Compact),
            Text(Status, foreground: Data.Appearance.HighContrast ? (Color? )null : Data.Appearance.Muted)
                .FontSize(11f)
                .Visible(Data.Compact)
                .Automation(new()
                {
                    Name = Status,
                    AutomationId = "instances.status." + Data.Item.Id
                }),
        ])
            .Spacing(5f)
            .Weight(1f)
            .Shrink(1f)
            .MinContentWidth(0f)
            .VAlign(VerticalAlignment.Center),
    ])
        .Spacing(12f)
        .VAlign(VerticalAlignment.Center);
}

[View]
internal readonly partial record struct InstanceRuntimeCell(LauncherAppearance Theme, InstanceListRowState Data, InstancesPage Page)
{
    internal void OpenContent() => Page.Open(Data.Item.Id, InstanceAction.Content);

    public Ui Body => Column([
        Text(Data.Runtime, foreground: Data.Appearance.HighContrast ? (Color? )null : Data.Appearance.Ink)
            .FontSize(12f)
            .Wrap(),
        Text(Data.Configuration, foreground: Data.Appearance.HighContrast ? (Color? )null : Data.Appearance.Muted)
            .FontSize(11f)
            .Wrap(),
        Button(Text(Data.Content)
                .FontSize(11f)
                .Wrap(), OpenContent)
            .Style(Data.Appearance.Content)
            .HAlign(HorizontalAlignment.Left)
            .Automation(new()
            {
                Name = string.Format(Resources.InstanceManageAction, Resources.InstanceContentDetails, Data.Item.Name),
                AutomationId = "instances.compact.content." + Data.Item.Id
            })
            .Visible(Data.Compact),
    ])
        .Spacing(5f)
        .VAlign(VerticalAlignment.Center);
}

[View]
internal readonly partial record struct InstanceActionsCell(LauncherAppearance Theme, InstanceListRowState Data, InstancesPage Page)
{
    internal Action LaunchAction
    {
        get
        {
            var page = Page;
            var id = Data.Item.Id;
            return () => page.Launch(id);
        }
    }

    internal Action ConfigureAction
    {
        get
        {
            var page = Page;
            var id = Data.Item.Id;
            return () => page.Open(id, InstanceAction.Configure);
        }
    }

    internal Action MenuAction
    {
        get
        {
            var page = Page;
            var item = Data.Item;
            return () => page.ShowMenu(item);
        }
    }

    internal bool Busy => Data.Pending || Data.Launch?.Busy == true;
    internal string LaunchLabel => Data.InstallationIncomplete
        ? Resources.CreateResumeInstallation
        : Data.Item.Instance.ServerId is null ? Resources.LaunchGame : Resources.ServerConnect;

    public Ui Body => Row([
        LauncherControls.Command(Theme,Text(LaunchLabel)
                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(), LaunchAction)
            .Style(Data.Appearance.Command)
            .Enabled(!Busy && (Data.InstallationIncomplete || Data.Item.CanLaunch))
            .Weight(1f)
            .Shrink(1f)
            .MinContentWidth(0f)
            .Automation(new()
            {
                Name = string.Format(Resources.InstanceManageAction, LaunchLabel, Data.Item.Name),
                HelpText = Data.LaunchBlockReason,
                AutomationId = "instances.launch." + Data.Item.Id
            }),
        LauncherControls.OutlineCommand(
Theme,            Row([
                LauncherIcons.Line("wrench", 18f)
            ]),
            ConfigureAction)
            .Style(Theme.Buttons.FlatIconButton)
            .Width(32f).Height(32f)
            .Enabled(!Busy)
            .ToolTip(Resources.CurrentInstanceSettings)
            .Automation(new()
            {
                Name = string.Format(Resources.InstanceManageAction, Resources.CurrentInstanceSettings, Data.Item.Name),
                AutomationId = "instances.configure." + Data.Item.Id
            }),
        LauncherControls.Command(
Theme,            Row([
                LauncherIcons.Line("more", 16f)
            ]),
            MenuAction)
            .Style(Data.Appearance.Icon.Padding(0f))
            .Width(28f).Height(28f)
            .Automation(new()
            {
                Name = string.Format(Resources.InstanceManageContext, Data.Item.Name),
                AutomationId = "instances.more." + Data.Item.Id
            }),
    ])
        .Spacing(8f)
        .VAlign(VerticalAlignment.Center);
}

[View]
internal readonly partial record struct InstanceListHeading(bool Compact, InstanceListAppearance Appearance)
{
    public Ui Body => Border(Grid(
            InstancesPage.Columns(Compact),
            [GridLength.Auto],
            [
                Text(Resources.InstanceNameColumn)
                    .FontSize(11f).Foreground(Appearance.Muted)
                    .Cell(0, 0),
                Text(Resources.InstanceRuntimeColumn)
                    .FontSize(11f).Foreground(Appearance.Muted)
                    .Cell(1, 0),
                Text(Resources.InstanceContentColumn)
                    .FontSize(11f).Foreground(Appearance.Muted)
                    .Visible(!Compact)
                    .Cell(2, 0),
                Text(Resources.InstanceStatusColumn)
                    .FontSize(11f).Foreground(Appearance.Muted)
                    .Visible(!Compact)
                    .Cell(Compact ? 1 : 3, 0),
                Text(Resources.InstanceActionsColumn)
                    .FontSize(11f).Foreground(Appearance.Muted)
                    .Cell(Compact ? 2 : 4, 0),
            ])
            .ColumnSpacing(16f)
            .Padding(Compact ? 16f : 20f, 12f))
        .BorderBrush(Appearance.Edge)
        .BorderThickness(0f, 0f, 0f, 1f);
}