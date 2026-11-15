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

internal sealed partial class ExternalAccountNotice(ExternalAccountPopup owner) : Control
{
    private Ui Body => Border(Grid(
            [GridLength.Pixels(20f), GridLength.Star()],
            [GridLength.Auto],
            [
                LauncherIcons.Line("warning", 20f, LauncherTheme.Error)
                    .Cell(0, 0),
                Column([
                    Grid(
                        [GridLength.Star(), GridLength.Pixels(24f)],
                        [GridLength.Auto],
                        [
                            Text(owner.ErrorTitle)
                                .FontSize(14f).FontWeight(600).Foreground(LauncherTheme.Ink)
                                .Wrap()
                                .Automation(new()
                                {
                                    Name = owner.ErrorTitle,
                                    AutomationId = "accounts.external.error",
                                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                                })
                                .Cell(0, 0),
                            LauncherControls.Command(
                                Row([
                                    LauncherIcons.Line("info", 16f)
                                ]),
                                () => owner.ShowDetails())
                                .Style(LauncherButtons.IconButton.Padding(4f))
                                .Width(24f).Height(24f)
                                .VAlign(VerticalAlignment.Center)
                                .Visible(owner.Original.Length > 0)
                                .ToolTip(Resources.ExternalErrorDetails)
                                .Automation(new()
                                {
                                    Name = Resources.ExternalErrorDetails,
                                    AutomationId = "accounts.external.error.info"
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(8f),
                    Text(owner.ErrorDetail)
                        .FontSize(13f).Foreground(LauncherTheme.Muted)
                        .Wrap()
                        .Visible(owner.ErrorDetail.Length > 0),
                ])
                    .Spacing(4f)
                    .Cell(1, 0),
            ])
            .ColumnSpacing(12f))
        .Padding(12f)
        .Background(LauncherTheme.HighContrast ? LauncherTheme.ControlBackground : LauncherTheme.Error.MultiplyAlpha(.07f))
        .BorderBrush(LauncherTheme.Error.MultiplyAlpha(.32f))
        .BorderThickness(1f);
}

/// <summary>A light-dismiss detail surface anchored to the notice's information button.</summary>
internal sealed partial class AccountErrorDetails(Window window, string original) : Control
{
    private Ui Body => Column([
        Border(Row([
                Text(Resources.ExternalErrorDetails)
                    .FontSize(13f).FontWeight(600).Foreground(LauncherTheme.Ink)
                    .Weight(1f)
                    .VAlign(VerticalAlignment.Center),
                LauncherControls.Command(
                    Row([
                        LauncherIcons.Line("copy", 16f)
                    ]),
                    Copy)
                    .Style(LauncherButtons.IconButton.Padding(4f))
                    .Width(24f).Height(24f)
                    .Automation(new()
                    {
                        Name = Resources.CopyLaunchError,
                        AutomationId = "accounts.external.error.copy"
                    }),
            ])
                .Spacing(12f))
            .Padding(12f, 8f)
            .BorderBrush(LauncherTheme.Edge)
            .BorderThickness(0f, 0f, 0f, 1f),
        Flow(FlowDirection.LeftToRight, TextBox()
                .Text(original)
                .ReadOnly()
                .AcceptsReturn()
                .Wrap()
                .FontSize(12f)
                .Style(LauncherStyles.Input)
                .Padding(12f)
                .MaxHeight(Math.Min(240f, window.ClientSize.Height - 160f))
                .Automation(new()
                {
                    Name = Resources.ExternalErrorDetails,
                    AutomationId = "accounts.external.error.text"
                })),
    ])
        .Width(Math.Min(360f, window.ClientSize.Width - 32f));

    private void Copy()
    {
        if (Sprout.Host.Clipboard.SetText(original)) LauncherToast.Show(window, Resources.LaunchErrorCopied);
        else LauncherToast.ShowTransientError(window, Resources.ExternalCopyFailed);
    }
}