// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Runtime;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Runtime;

[View]
internal readonly partial record struct RuntimeSelector(
    LauncherAppearance Theme,
    string[] Values,
    int Index,
    Action<int> Changed,
    string Name,
    string Id,
    bool Enabled,
    string Placeholder = "")
{
    public Ui Body => LauncherControls.Choice(Theme, Values)
        .SelectedIndex(Index)
        .OnSelectionChanged(Changed)
        .Enabled(Enabled)
        .Placeholder(Placeholder)
        .MinHeight(40f)
        .Automation(new()
        {
            Name = Name,
            AutomationId = Id
        });
}

[View]
internal readonly partial record struct RuntimeVersion(
    LauncherAppearance Theme,
    string[] Values,
    string Value,
    Action<string> Changed,
    string Name,
    string Id,
    bool Enabled)
{
    public Ui Body => LauncherControls.Choice(Theme, Values)
        .IsEditable(true)
        .Text(Value)
        .OnTextChanged(Changed)
        .FilterMode(ComboBoxFilterMode.Contains)
        .Enabled(Enabled)
        .MinHeight(40f)
        .Automation(new()
        {
            Name = Name,
            AutomationId = Id,
            HelpText = Resources.RuntimeVersionRequired
        });
}

[View]
internal readonly partial record struct RuntimeChoiceField(
    LauncherAppearance Theme,
    string Title,
    string[] Values,
    int Index,
    Action<int> Changed,
    string Id,
    bool Enabled)
{
    public Ui Body => Column([
        Text(Title)
            .FontSize(15f).Foreground(Theme.Ink)
            .Wrap(),
        new RuntimeSelector(
Theme,            Values,
            Index,
            Changed,
            Title,
            Id,
            Enabled),
    ])
        .Spacing(8f);
}

[View]
internal readonly partial record struct RuntimeVersionField(
    LauncherAppearance Theme,
    string Title,
    string[] Values,
    string Value,
    Action<string> Changed,
    string Id,
    bool Enabled)
{
    public Ui Body => Column([
        Text(Title)
            .FontSize(15f).Foreground(Theme.Ink)
            .Wrap(),
        new RuntimeVersion(
Theme,            Values,
            Value,
            Changed,
            Title,
            Id,
            Enabled),
    ])
        .Spacing(8f);
}

[View]
internal readonly partial record struct RuntimeComponentGrid(LauncherAppearance Theme, RuntimeState State, RuntimeController Controller, bool Compact)
{
    internal bool Editable => !State.Active && !State.Loading;
    internal GridLength[] Columns => Compact ? [GridLength.Star()] : [GridLength.Star(), GridLength.Star()];
    internal GridLength[] Rows => State.UseInstalled
        ? Compact ? [GridLength.Auto, GridLength.Auto] : [GridLength.Auto]
        : Compact
        ? State.Loader == "vanilla" ? [GridLength.Auto, GridLength.Auto, GridLength.Auto] : [GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto]
        : [GridLength.Auto, GridLength.Auto];
    internal int SecondColumn => Compact ? 0 : 1;
    internal int SecondRow => Compact ? 1 : 0;
    internal int LoaderRow => State.UseInstalled
        ? 0
        : Compact ? 2 : 1;
    internal int LoaderVersionRow => State.UseInstalled
        ? 0
        : Compact && State.Loader != "vanilla" ? 3 : LoaderRow;

    public Ui Body => Grid(
        Columns,
        Rows,
        [
            new RuntimeChoiceField(
Theme,                Resources.RuntimeChangeMode,
                [Resources.RuntimeOnlineMode, Resources.RuntimeInstalledMode],
                State.UseInstalled ? 1 : 0,
                Controller.SetMode,
                "runtime.mode",
                Editable).Cell(0, 0),
            new RuntimeChoiceField(
Theme,                Resources.RuntimeInstalledProfile,
                State.Installed,
                Array.IndexOf(State.Installed, State.Profile),
                Controller.SetProfile,
                "runtime.profile",
                Editable).Cell(SecondColumn, SecondRow)
                .Visible(State.UseInstalled),
            new RuntimeVersionField(
Theme,                Resources.RuntimeMinecraft,
                State.MinecraftVersions,
                State.Minecraft,
                Controller.SetMinecraft,
                "runtime.minecraft",
                Editable).Cell(SecondColumn, SecondRow)
                .Visible(!State.UseInstalled),
            new RuntimeChoiceField(
Theme,                Resources.RuntimeLoader,
                [Resources.RuntimeVanilla, "Fabric", "Quilt", "Forge", "NeoForge"],
                Array.IndexOf(["vanilla", "fabric", "quilt", "forge", "neoforge"], State.Loader),
                Controller.SetLoader,
                "runtime.loader",
                Editable).Cell(0, LoaderRow)
                .Visible(!State.UseInstalled),
            new RuntimeVersionField(
Theme,                Resources.RuntimeLoaderVersion,
                State.LoaderVersions,
                State.LoaderVersion,
                Controller.SetLoaderVersion,
                "runtime.loader-version",
                Editable).Cell(SecondColumn, LoaderVersionRow)
                .Visible(!State.UseInstalled && State.Loader != "vanilla"),
        ])
        .ColumnSpacing(24f).RowSpacing(16f);
}

[View]
internal readonly partial record struct RuntimeFields(
    LauncherAppearance Theme,
    RuntimeState State,
    RuntimeController Controller,
    RuntimePage Owner,
    bool Compact)
{
    internal bool Editable => !State.Active && !State.Loading;
    internal Action LoadVersionsAction
    {
        get
        {
            var controller = Controller;
            return () => _ = controller.LoadVersionsAsync();
        }
    }

    public Ui Body => Column([
        Text(Resources.RuntimeCurrent)
            .FontSize(18f).FontWeight(700).Foreground(Theme.Ink),
        Text(State.Current?.VersionId ?? Resources.ValueNotAvailable)
            .FontSize(16f).Foreground(Theme.Ink)
            .Wrap()
            .Automation(new()
            {
                Name = State.Current?.VersionId ?? Resources.ValueNotAvailable,
                AutomationId = "runtime.current"
            }),
        Text(string.Format(Resources.RuntimeJavaRecommended, (State.Current as ResolvedRuntimeStatus)?.JavaVersion?["majorVersion"] ?? Resources.ValueNotAvailable))
            .FontSize(12f).Foreground(Theme.Muted)
            .Wrap(),
        new RuntimeComponentGrid(Theme,State, Controller, Compact),
        Column([
            LauncherControls.OutlineCommand(Theme,Resources.RuntimeLoadVersions, LoadVersionsAction)
                .Enabled(Editable)
                .MinHeight(36f)
                .HAlign(HorizontalAlignment.Left)
                .Automation(new()
                {
                    Name = Resources.RuntimeLoadVersions,
                    AutomationId = "runtime.catalog"
                }),
            Text(string.Format(Resources.RuntimeCatalogSource, State.CatalogLabel))
                .FontSize(12f).Foreground(Theme.Muted)
                .Wrap()
                .Visible(State.CatalogSource.Length > 0),
        ])
            .Spacing(10f)
            .Visible(!State.UseInstalled),
        Text(Resources.RuntimeKeepFiles)
            .FontSize(12f).Foreground(Theme.Muted)
            .Wrap(),
        Text(Resources.RuntimeHistory)
            .FontSize(18f).FontWeight(700).Foreground(Theme.Ink)
            .Margin(new Thickness(0f, 12f, 0f, 0f))
            .Visible(State.History.Length > 0),
        ItemsRepeater(State.History, static operation => operation.Id, Owner.HistoryFactory, new StackLayout { EstimatedItemExtent = 52f }).KeyboardNavigation(),
    ])
        .Spacing(16f)
        .Padding(24f);
}