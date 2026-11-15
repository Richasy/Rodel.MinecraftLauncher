// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

/// <summary>Renders the approved compact account flow, keeping header and actions outside the single scrolling body.</summary>
internal sealed partial class ExternalAccountBody(ExternalAccountPopup owner) : Control
{
    private readonly ExternalAccountContent _content = new(owner);
    private readonly ExternalAccountFooter _footer = new(owner);
    private float Width => Math.Min(440f, owner.Feature.Window.ClientSize.Width - 48f);

    private Ui Body => Column([
        Border(Row([
                LauncherControls.Command(
                    Row([
                        LauncherIcons.Line("back", 16f)
                    ]),
                    () => owner.Back())
                    .Style(LauncherButtons.IconButton)
                    .Width(28f).Height(28f)
                    .Visible(!owner.Waiting)
                    .VAlign(VerticalAlignment.Center)
                    .Automation(new()
                    {
                        Name = Resources.BackToOperation,
                        AutomationId = "accounts.external.back"
                    }),
                Text(owner.Title)
                    .FontSize(16f).FontWeight(600).Foreground(LauncherTheme.Ink)
                    .Wrap()
                    .Weight(1f)
                    .VAlign(VerticalAlignment.Center),
                LauncherControls.Command(
                    Row([
                        LauncherIcons.Line("close", 16f)
                    ]),
                    () => owner.Dismiss())
                    .Style(LauncherButtons.PopupCloseButton)
                    .Width(28f).Height(28f)
                    .Visible(!owner.Waiting && owner.Page != "device")
                    .VAlign(VerticalAlignment.Center)
                    .Automation(new()
                    {
                        Name = Resources.CloseLaunchSettings,
                        AutomationId = "accounts.external.close"
                    }),
            ])
                .Spacing(8f))
            .Padding(16f, 12f)
            .BorderBrush(LauncherTheme.Edge)
            .BorderThickness(0f, 0f, 0f, 1f),
        ScrollView(_content)
            .MaxHeight(Math.Max(100f, owner.Feature.Window.ClientSize.Height - 210f))
            .Automation(new()
            {
                Name = owner.Title,
                AutomationId = "accounts.external.scroll"
            }),
        _footer.Visible(owner.Page is not ("sources" or "waiting")),
    ])
        .Width(Width);
}

internal sealed partial class ExternalAccountContent(ExternalAccountPopup owner) : Control
{
    private readonly ExternalAccountNotice _notice = new(owner);
    private readonly ExternalAccountSources _sources = new(owner);
    private readonly ExternalAccountPassword _password = new(owner);
    private readonly ExternalAccountDevice _device = new(owner);
    private readonly ExternalAccountRoles _roles = new(owner);

    private Ui Body => Column([
        _notice.Visible(owner.ErrorTitle.Length > 0),
        _sources.Visible(owner.Page == "sources"),
        _password.Visible(owner.Page == "password"),
        _device.Visible(owner.Page == "device"),
        Row([
            ProgressRing(new Size(20f, 20f))
                .Style(default(ProgressRingStyle).Foreground(LauncherTheme.ProgressAccent)
                    .Thickness(2f))
                .VAlign(VerticalAlignment.Center),
            Column([
                Text(owner.Activity)
                    .FontSize(14f).Foreground(LauncherTheme.Ink)
                    .Automation(new()
                    {
                        Name = owner.Activity,
                        AutomationId = "accounts.external.waiting",
                        LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                    }),
                Text(owner.Source)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Wrap(),
            ])
                .Spacing(0f)
                .Weight(1f)
                .VAlign(VerticalAlignment.Center),
            LauncherControls.Command(Resources.DialogCancel, () => owner.Cancel())
                .Style(LauncherButtons.IconButton)
                .VAlign(VerticalAlignment.Center)
                .Automation(new()
                {
                    Name = Resources.AccountCancelLogin,
                    AutomationId = "accounts.external.cancel"
                }),
        ])
            .Spacing(12f)
            .Visible(owner.Waiting),
        _roles.WithRoles(owner.Attempt?.Profiles, owner.SelectedRole)
            .Visible(owner.Page == "roles"),
        Column([
            Text(string.Format(Resources.ExternalNoRolesHint, owner.Source))
                .FontSize(13f).Foreground(LauncherTheme.Muted)
                .Wrap(),
            LauncherControls.Command(Resources.ExternalManageRoles, () => owner.OpenRoleManager())
                .Style(LauncherButtons.IconButton)
                .HAlign(HorizontalAlignment.Left)
                .Automation(new()
                {
                    Name = Resources.ExternalManageRoles,
                    AutomationId = "accounts.external.manage-roles"
                }),
        ])
            .Spacing(12f)
            .Visible(owner.Page == "empty"),
    ])
        .Spacing(16f)
        .Padding(16f);
}

internal sealed partial class ExternalAccountFooter(ExternalAccountPopup owner) : Control
{
    private Ui Body => Column([
        LauncherControls.Command(Resources.ExternalLogin, () => owner.Login())
            .Style(LauncherButtons.DialogPrimary)
            .Enabled(!owner.Busy && owner.Username.Trim()
                .Length > 0 && owner.Password.Length > 0 && (owner.Target is not null || owner.Address.Trim()
                .Length > 0))
            .Visible(owner.Page == "password")
            .Automation(new()
            {
                Name = Resources.ExternalLogin,
                AutomationId = "accounts.external.login"
            }),
        Grid(
            [GridLength.Star(), GridLength.Star()],
            [GridLength.Auto],
            [
                LauncherControls.Command(owner.DeviceRetry ? Resources.ExternalRetryConnection : Resources.ExternalOpenPage, () =>
                    {
                        if (owner.DeviceRetry)
                            owner.RetryDevice();
                        else
                            owner.OpenAuthorization();
                    })
                    .Style(LauncherButtons.DialogPrimary)
                    .Automation(new()
                    {
                        Name = owner.DeviceRetry ? Resources.ExternalRetryConnection : Resources.ExternalOpenPage,
                        AutomationId = "accounts.external.open"
                    })
                    .Cell(0, 0),
                LauncherControls.OutlineCommand(Resources.DialogCancel, () => owner.Cancel())
                    .Style(LauncherButtons.DialogSecondary)
                    .Automation(new()
                    {
                        Name = Resources.DialogCancel,
                        AutomationId = "accounts.external.cancel"
                    })
                    .Cell(1, 0),
            ])
            .ColumnSpacing(12f)
            .Visible(owner.Page == "device" && !owner.DeviceEnded),
        LauncherControls.Command(Resources.ExternalRestartCode, () => owner.LittleSkin())
            .Style(LauncherButtons.DialogPrimary)
            .Visible(owner.Page == "device" && owner.DeviceEnded)
            .Automation(new()
            {
                Name = Resources.ExternalRestartCode,
                AutomationId = "accounts.external.restart"
            }),
        LauncherControls.Command(Resources.ExternalAddRole, () => owner.SelectRole())
            .Style(LauncherButtons.DialogPrimary)
            .Enabled(owner.SelectedRole.Length > 0 && !owner.Busy)
            .Visible(owner.Page == "roles")
            .Automation(new()
            {
                Name = Resources.ExternalAddRole,
                AutomationId = "accounts.external.select-role"
            }),
        LauncherControls.Command(Resources.ExternalRereadRoles, () => owner.RereadRoles())
            .Style(LauncherButtons.DialogPrimary)
            .Visible(owner.Page == "empty")
            .Automation(new()
            {
                Name = Resources.ExternalRereadRoles,
                AutomationId = "accounts.external.reread"
            }),
        LauncherControls.Command(Resources.AccountRetry, () => owner.Refresh())
            .Style(LauncherButtons.DialogPrimary)
            .Visible(owner.Page == "refresh")
            .Automation(new()
            {
                Name = Resources.AccountRetry,
                AutomationId = "accounts.external.refresh"
            }),
        LauncherControls.Command(Resources.AccountSignInAgain, () => owner.Reauthenticate())
            .Style(LauncherButtons.DialogPrimary)
            .Visible(owner.Page == "reauth")
            .Automation(new()
            {
                Name = Resources.AccountSignInAgain,
                AutomationId = "accounts.external.reauthenticate"
            }),
        LauncherControls.Command(Resources.ExternalAddOtherRole, () => owner.Feature.ShowExternal())
            .Style(LauncherButtons.DialogPrimary)
            .Visible(owner.Page == "deleted")
            .Automation(new()
            {
                Name = Resources.ExternalAddOtherRole,
                AutomationId = "accounts.external.add-other"
            }),
    ])
        .Spacing(0f)
        .Padding(16f, 0f, 16f, 16f);
}