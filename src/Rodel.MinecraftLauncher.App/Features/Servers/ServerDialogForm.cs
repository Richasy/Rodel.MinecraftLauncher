// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Servers;

internal sealed record ServerFormState(
    ServerAction Action,
    string Name,
    string Address,
    string AddressError,
    string[] InstanceNames,
    int InstanceIndex,
    string[] IdentityNames,
    int AccountIndex,
    bool Remember,
    ServerAssociationData[] Associations,
    string[] PreferredNames,
    int PreferredIndex,
    bool Loading,
    bool Active,
    bool Started,
    string Error,
    string FavoriteName,
    bool StatusUnavailable,
    bool NoInstances,
    string Language,
    Brush Ink,
    Brush Muted,
    Brush ErrorBrush,
    bool RightToLeft)
{
    internal bool Connecting => Action is ServerAction.Connect or ServerAction.Direct;
}

[View]
internal readonly partial record struct ServerDialogForm(LauncherAppearance Theme, ServerDialog Owner, ServerFormState Data)
{
    internal static Func<ServerAssociationData, ServerAssociationRow> AssociationTemplate(LauncherAppearance theme) => row => new ServerAssociationRow(theme, row);

    public Ui Body => Column([
        Column([
            Text(Resources.ServerName)
                .FontSize(12f).Foreground(Data.Ink),
            TextBox(Data.Name, Owner.ChangeName)
                .Enabled(!Data.Active)
                .FontSize(13f)
                .Padding(12f, 10f)
                .Automation(new()
                {
                    Name = Resources.ServerName,
                    AutomationId = "servers.name"
                }),
        ])
            .Spacing(8f)
            .Visible(Data.Action is ServerAction.Add or ServerAction.Edit),
        Column([
            Text(Resources.ServerAddress)
                .FontSize(12f).Foreground(Data.Ink),
            Flow(FlowDirection.LeftToRight, TextBox(Data.Address, Owner.ChangeAddress)
                    .Enabled(!Data.Active)
                    .FontSize(13f)
                    .Padding(12f, 10f)
                    .Automation(new()
                    {
                        Name = Resources.ServerAddress,
                        AutomationId = "servers.address"
                    })),
            Text(Data.AddressError)
                .FontSize(12f).Foreground(Data.ErrorBrush)
                .Wrap()
                .Visible(Data.AddressError.Length > 0),
            Text(Resources.ServerAddressHint)
                .FontSize(12f).Foreground(Data.Muted)
                .Wrap(),
        ])
            .Spacing(8f)
            .Visible(Data.Action is ServerAction.Add or ServerAction.Edit or ServerAction.Direct),
        Flow(FlowDirection.LeftToRight, Text(Data.Address)
                .FontSize(13f).Foreground(Data.Ink)
                .Wrap()
                .Align(Data.RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading))
            .Visible(Data.Action == ServerAction.Connect),
        Text(Resources.ServerStatusConnectAnyway)
            .FontSize(12f).Foreground(Data.Muted)
            .Wrap()
            .Visible(Data.Action == ServerAction.Connect && Data.StatusUnavailable),
        Text(Resources.ServerClaimClientDescription)
            .FontSize(13f).Foreground(Data.Muted)
            .Wrap()
            .Visible(Data.Action == ServerAction.ClaimClient),
        Column([
            Text(Resources.ServerClientInstance)
                .FontSize(12f).Foreground(Data.Ink),
            LauncherControls.Choice(Theme,Data.InstanceNames).MinHeight(40f)
                .SelectedIndex(Data.InstanceIndex)
                .OnSelectionChanged(Owner.ChooseInstance)
                .Enabled(!Data.Active && !Data.Started && !Data.NoInstances)
                .Automation(new()
                {
                    Name = Resources.ServerClientInstance,
                    AutomationId = "servers.instance"
                }),
            LauncherControls.OutlineCommand(Theme,Resources.ServerShowLocal, Owner.ShowLocal)
                .Visible(Data.NoInstances)
                .Enabled(!Data.Active)
                .Automation(new()
                {
                    Name = Resources.ServerShowLocal,
                    AutomationId = "servers.local"
                }),
        ])
            .Spacing(8f)
            .Visible(Data.Connecting || Data.Action == ServerAction.ClaimClient),
        Column([
            Text(Resources.ServerIdentity)
                .FontSize(12f).Foreground(Data.Ink),
            LauncherControls.Choice(Theme,Data.IdentityNames).MinHeight(40f)
                .SelectedIndex(Data.AccountIndex)
                .OnSelectionChanged(Owner.ChooseAccount)
                .Enabled(!Data.Active && !Data.Started && !Data.Loading)
                .Automation(new()
                {
                    Name = Resources.ServerIdentity,
                    AutomationId = "servers.identity"
                }),
        ])
            .Spacing(8f)
            .Visible(Data.Connecting),
        new LauncherCheckBox(
Theme,            Resources.ServerRemember,
            Data.Remember,
            Owner.ToggleRemember,
            !Data.Active && !Data.Started,
            "servers.remember").Visible(Data.Action == ServerAction.Connect),
        Column([
            Text(Resources.ServerAssociationHint)
                .FontSize(12f).Foreground(Data.Muted)
                .Wrap(),
            ItemsRepeater(Data.Associations, static row => row.Id, AssociationTemplate(Theme), new StackLayout { EstimatedItemExtent = 40f }).KeyboardNavigation(),
            Text(Resources.ServerPreferred)
                .FontSize(12f).Foreground(Data.Ink),
            LauncherControls.Choice(Theme,Data.PreferredNames).MinHeight(40f)
                .SelectedIndex(Data.PreferredIndex)
                .OnSelectionChanged(Owner.ChoosePreferred)
                .Enabled(!Data.Active)
                .Automation(new()
                {
                    Name = Resources.ServerPreferred,
                    AutomationId = "servers.preferred"
                }),
        ])
            .Spacing(10f)
            .Visible(Data.Action == ServerAction.Associate),
        Text(string.Format(Resources.ServerRemoveConfirmation, Data.FavoriteName))
            .FontSize(14f).Foreground(Data.Ink)
            .Wrap()
            .Visible(Data.Action == ServerAction.Remove),
        Text(Resources.ServerRemoveHint)
            .FontSize(12f).Foreground(Data.Muted)
            .Wrap()
            .Visible(Data.Action == ServerAction.Remove),
        ProgressBar()
            .Indeterminate(true)
            .Thickness(3f)
            .Visible(Data.Loading || Data.Active),
        Text(Data.Error)
            .FontSize(12f).Foreground(Data.ErrorBrush)
            .Wrap()
            .Visible(Data.Error.Length > 0)
            .Automation(new()
            {
                Name = Data.Error,
                AutomationId = "servers.operation.error",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
    ])
        .Spacing(16f)
        .Padding(24f, 20f);
}