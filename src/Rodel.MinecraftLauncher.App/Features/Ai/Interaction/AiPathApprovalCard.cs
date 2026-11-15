// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Presents the host path gate in its originating turn, independently of model questions.</summary>
internal sealed partial class AiPathApprovalCard(Func<AiApproval?> approval, Func<int> revision) : Control
{
    private AiApproval? Approval
    {
        get
        {
            _ = revision();
            return approval();
        }
    }

    private string PathText => Approval?.PathAccess?.GetProperty("path").GetString() ?? string.Empty;
    private string ParentText => Approval?.PathAccess?.GetProperty("parentPath").GetString() ?? string.Empty;
    private bool Available => Approval is { IsActive: true, Deciding: false };

    private void Decide(bool allow)
    {
        if (Approval is { } current)
            _ = current.DecideAsync(allow);
    }

    private Ui Body => Border(Column([
            Text(Resources.AiPathAccessTitle)
                .FontSize(14f).FontWeight(500).Foreground(LauncherTheme.Ink),
            Text(Approval?.PathAccess?.GetProperty("mode")
                    .GetString() == "read_write" ? Resources.AiPathAccessWrite : Resources.AiPathAccessRead)
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Wrap(),
            Flow(FlowDirection.LeftToRight, SelectableText(Text(PathText)
                        .FontSize(13f).Foreground(LauncherTheme.Ink)
                        .Wrap())
                    .Automation(new()
                    {
                        Name = PathText,
                        AutomationId = "ai.path.target"
                    })),
            LauncherControls.Choice([Resources.AiPathScopeOnce, Resources.AiPathScopeParent, Resources.AiPathScopeAll])
                .SelectedIndex(Approval?.PathScope ?? 0)
                .OnSelectionChanged(index => Approval?.SelectPathScope(index))
                .MinHeight(40f)
                .Enabled(Available)
                .Automation(new()
                {
                    Name = Resources.AiApprovalScope,
                    AutomationId = "ai.path.scope"
                }),
            Flow(FlowDirection.LeftToRight, Text(ParentText)
                    .FontSize(11f).Foreground(LauncherTheme.Muted)
                    .Wrap())
                .Visible(Approval?.PathScope == 1),
            Text(Approval?.Error ?? string.Empty)
                .FontSize(11f).Foreground(LauncherTheme.Error)
                .Wrap()
                .Visible(!string.IsNullOrEmpty(Approval?.Error)),
            Row([
                LauncherControls.Command(Resources.AiApprovalAllow, () => Decide(true))
                    .AsPrimary()
                    .Style(LauncherButtons.Accent)
                    .Enabled(Available)
                    .Automation(new()
                    {
                        Name = Resources.AiApprovalAllow,
                        AutomationId = "ai.path.approve"
                    }),
                LauncherControls.OutlineCommand(Resources.AiApprovalDecline, () => Decide(false))
                    .Enabled(Available)
                    .Automation(new()
                    {
                        Name = Resources.AiApprovalDecline,
                        AutomationId = "ai.path.decline"
                    }),
            ])
                .Spacing(8f)
                .HAlign(HorizontalAlignment.Right),
        ])
            .Spacing(8f))
        .Padding(12f)
        .Background(LauncherTheme.ControlBackground)
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(1f);
}