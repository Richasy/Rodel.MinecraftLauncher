// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Globalization;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Keeps the current exact approval above the composer without opening a modal for ordinary permission decisions.</summary>
internal sealed partial class AiApprovalBanner(AiPage owner) : Control
{
    private AiApproval? Approval
    {
        get
        {
            _ = owner.Revision;
            return owner.Approval;
        }
    }

    private bool Compact => owner.Viewport.Width < 960f;
    private static AiDialogAppearance Appearance => AiDialogAppearance.Capture();
    private GridLength[] Columns => Compact ? [GridLength.Star()] : [GridLength.Star(), GridLength.Auto];
    private GridLength[] Rows => Compact ? [GridLength.Auto, GridLength.Auto] : [GridLength.Auto];
    private bool Active => Approval is { IsActive: true };
    private bool Permission => Approval?.OperationPermission == true;
    private bool Available => Active && Approval?.Deciding == false && !owner.Busy;
    private string Name => Approval?.Name ?? string.Empty;
    private string Error => Approval?.Error ?? string.Empty;
    private Action DetailsAction
    {
        get
        {
            var approval = Approval;
            return () =>
            {
                if (approval is not null)
                    owner.ShowReview(approval);
            };
        }
    }

    private MenuFlyout ApprovalMenu
    {
        get
        {
            var action = Decision(true, "tool");
            return new MenuFlyout(new MenuFlyoutItem(string.Format(Resources.AiApprovalAllowTool, Name), () => DispatcherQueue.GetForCurrentThread().TryEnqueue(action))
            {
                IsEnabled = Permission && Available,
                Automation = new() { AutomationId = "ai.approval.allow-tool" },
            }).Style(LauncherMenus.CommandMenu);
        }
    }

    private Action Decision(bool approve, string scope = "once")
    {
        var approval = Approval;
        return () =>
        {
            if (approval is not null)
                _ = approval.DecideAsync(approve, scope);
        };
    }

    /// <summary>Builds compact approval actions, optional scope and an explicit entry to the complete frozen details.</summary>
    private Ui Body => Border(Column([
            Grid(
                Columns,
                Rows,
                [
                    Column([
                        Text(Permission ? Resources.AiOperationPermission : Resources.AiReview)
                            .FontSize(12f).FontWeight(500).Foreground(Appearance.Ink),
                        Flow(FlowDirection.LeftToRight, Text(Name)
                                .FontSize(11f).Foreground(LauncherTheme.Muted)
                                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                                .Automation(new()
                                {
                                    Name = Name,
                                    AutomationId = "ai.approval.tool"
                                })),
                    ])
                        .Spacing(3f)
                        .VAlign(VerticalAlignment.Center)
                        .Cell(0, 0),
                    Wrap([
                        LauncherControls.OutlineCommand(Resources.AiDetails, DetailsAction)
                            .Style(LauncherButtons.AiTextAction.Padding(8f, 6f)
                                .LabelStyle(LauncherStyles.Text.FontSize(12f)))
                            .Enabled(Available && Approval?.Reading == false)
                            .Automation(new()
                            {
                                Name = Resources.AiDetails,
                                AutomationId = "ai.approval.details"
                            }),
                        SplitButton(new LauncherSplitButtonLabel(Resources.AiApprovalAllow, LauncherStyles.Text.FontSize(12f).Foreground(LauncherTheme.PrimaryForeground)), Decision(true), ApprovalMenu)
                            .Style(LauncherMenus.AccentSplitCommand)
                            .CornerRadius(CornerRadius.Zero)
                            .Padding(new Thickness(10f, 6f, 10f, 6f))
                            .MinHeight(30f)
                            .SurfacePainter(LauncherSplitButtonSurface.PaintAccent)
                            .Enabled(Approval?.CanApprove == true && !owner.Busy)
                            .Automation(new()
                            {
                                Name = Resources.AiApprovalAllow,
                                AutomationId = "ai.approval.approve"
                            }),
                        LauncherControls.OutlineCommand(Resources.AiApprovalDecline, Decision(false))
                            .Style(Appearance.Secondary.Padding(10f, 6f)
                                .LabelStyle(LauncherStyles.Text.FontSize(12f)))
                            .Enabled(Available)
                            .Automation(new()
                            {
                                Name = Resources.AiApprovalDecline,
                                AutomationId = "ai.approval.decline"
                            }),
                    ])
                        .ItemSpacing(8f)
                        .LineSpacing(6f)
                        .VAlign(VerticalAlignment.Center).HAlign(CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right)
                        .Cell(Compact ? 0 : 1, Compact ? 1 : 0),
                ])
                .ColumnSpacing(12f).RowSpacing(8f),
            Text(Error)
                .FontSize(11f).Foreground(LauncherTheme.Error)
                .Wrap()
                .Visible(Error.Length > 0)
                .Automation(new()
                {
                    Name = Error,
                    AutomationId = "ai.approval.error"
                }),
        ])
            .Spacing(6f))
        .Padding(12f, 10f)
        .Background(LauncherTheme.ControlBackground)
        .BorderBrush(Appearance.Edge)
        .BorderThickness(1f);
}