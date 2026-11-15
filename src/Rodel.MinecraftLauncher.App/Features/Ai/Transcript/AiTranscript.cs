// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Renders actual Core turn snapshots and deltas, with recoverable errors and exact review entry points.</summary>
internal sealed partial class AiTranscript(AiPage owner) : Control
{
    private readonly AiWelcome _welcome = new(owner);
    private partial bool ErrorDetails { get; set; }
    private static Color LoadingBase => LauncherTheme.Muted.MultiplyAlpha(.14f);
    private static Color LoadingHighlight => LauncherTheme.Ink.MultiplyAlpha(.18f);
    private bool Loading
    {
        get
        {
            _ = owner.Revision;
            return owner.State.TranscriptActive && (owner.State.Loading || owner.Conversation.HistoryLoading);
        }
    }
    private AiUiTurn[] Turns
    {
        get
        {
            _ = owner.Revision;
            return owner.State.TranscriptActive && !Loading && !owner.Conversation.HistoryFailed ? [.. owner.Conversation.DisplayTurns] : [];
        }
    }

    private string ErrorText
    {
        get
        {
            _ = owner.Revision;
            return owner.State.Error.Length > 0 || owner.State.Loaded ? owner.State.Error : owner.State.Configuration.Error;
        }
    }

    private Ui Body => Border(Column([
            Column([
                Wrap([
                    Row([
                        LauncherIcons.Line("error", 14f, LauncherTheme.Error),
                        Text(Resources.AiFailed)
                            .Foreground(LauncherTheme.Error).FontSize(12f)
                            .Automation(new() { AutomationId = "ai.error" }),
                    ])
                        .Spacing(6f)
                        .VAlign(VerticalAlignment.Center),
                    LauncherControls.OutlineCommand(Text(ErrorDetails ? Resources.AiHideDetails : Resources.AiErrorDetails)
                            .FontSize(11f), () => ErrorDetails = !ErrorDetails)
                        .Style(LauncherButtons.AiTextAction)
                        .Automation(new() { AutomationId = "ai.error.details" }),
                    LauncherControls.OutlineCommand(Text(Resources.AiReload)
                            .FontSize(11f), () => _ = owner.State.LoadAsync())
                        .Style(LauncherButtons.AiTextAction)
                        .Enabled(!owner.State.Busy),
                ])
                    .ItemSpacing(8f)
                    .LineSpacing(4f),
                Border(Flow(FlowDirection.LeftToRight, SelectableText(Text(ErrorText)
                                .Foreground(LauncherTheme.Ink)
                                .Wrap()
                                .FontSize(12f))
                            .Automation(new() { AutomationId = "ai.error.original" })))
                    .Padding(14f)
                    .Background(LauncherTheme.ControlBackground)
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(1f)
                    .Visible(ErrorDetails),
            ])
                .Spacing(8f)
                .Visible(ErrorText.Length > 0),
            Column([
                Text(Resources.AiHistoryLoading)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Automation(new()
                    {
                        AutomationId = "ai.history.loading",
                        Name = Resources.AiHistoryLoading,
                        LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                    }),
                new AiHistoryShimmer(196f, 16f, Loading, LoadingBase, LoadingHighlight),
                new AiHistoryShimmer(320f, 28f, Loading, LoadingBase, LoadingHighlight),
                new AiHistoryShimmer(440f, 14f, Loading, LoadingBase, LoadingHighlight),
                new AiHistoryShimmer(388f, 14f, Loading, LoadingBase, LoadingHighlight),
                new AiHistoryShimmer(280f, 14f, Loading, LoadingBase, LoadingHighlight),
            ])
                .Spacing(18f)
                .Visible(Loading),
            _welcome.Visible(!Loading && !owner.Conversation.HistoryFailed && Turns.Length == 0 && owner.Conversation.RepairId is null),
            ItemsRepeater(
                Turns,
                static turn => turn.Id,
                turn => new AiTurnView(owner, turn),
                (view, turn) => view.Update(turn, owner.Revision),
                new AiTurnLayout(32f))
                .VirtualizationCache(0f),
        ])
            .Spacing(16f)
            .MaxWidth(LauncherLayout.AiConversationMaxWidth)
            .HAlign(HorizontalAlignment.Center))
        .Padding(20f, 24f, 20f, 28f);

}

/// <summary>Uses the owning transcript's explicit theme colors for one active history placeholder bar.</summary>
/// <param name="Width">The bar's preferred and maximum width within the transcript.</param>
/// <param name="Height">The bar's reserved line height.</param>
/// <param name="Active">Whether a real history request is still pending.</param>
/// <param name="BaseColor">The current theme's neutral placeholder surface.</param>
/// <param name="HighlightColor">The current theme's moving placeholder highlight.</param>
[View]
internal readonly partial record struct AiHistoryShimmer(float Width, float Height, bool Active, Color BaseColor, Color HighlightColor)
{
    /// <summary>Reserves one bounded skeleton line and animates it only during the pending read.</summary>
    public Ui Body => Shimmer(new Size(Width, Height))
        .MaxWidth(Width)
        .Height(Height)
        .Style(default(ShimmerStyle).CornerRadius(0f)
            .BaseColor(BaseColor)
            .HighlightColor(HighlightColor))
        .Active(Active);
}