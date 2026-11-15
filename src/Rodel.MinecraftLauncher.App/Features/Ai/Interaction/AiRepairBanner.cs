// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Presents authoritative Core repair state independently of the model's final answer.</summary>
internal sealed partial class AiRepairBanner(AiPage owner) : Control
{
    private AiRepairReport? Record
    {
        get
        {
            _ = owner.Revision;
            return owner.Conversation.Repair;
        }
    }

    private AiRepairPlanReport? Trial => Record?.Plans?.LastOrDefault();
    private string Status => Record?.NeedsModel == true
        ? Resources.AiRepairNeedsModel
        : !owner.Conversation.Active && owner.Conversation.Turns.LastOrDefault()?.Record.Status == "canceled" && Record?.Status != "unknown_effect" ? Resources.AiRepairPaused : Record?.Status switch
        {
            "planned" => Resources.AiRepairPlanned,
            "applying" => Resources.AiRepairApplying,
            "applied" => Resources.AiRepairApplied,
            "verifying" => Resources.AiRepairVerifying,
            "startup_checked" => Resources.AiRepairStartupChecked,
            "restored" => Resources.AiRepairRestored,
            "restoring" => Resources.AiRepairRestoring,
            "unknown_effect" => Resources.AiRepairUnknown,
            "paused" => Resources.AiRepairPaused,
            "failed" => Resources.AiRepairFailed,
            "needs_information" => Resources.AiRepairNeedsInformation,
            _ => Resources.AiRepairAnalyzing,
        };
    private bool CanRestore => !owner.Busy && !owner.Conversation.Active && Trial?.AfterSettings is not null && Trial?.State is not ("restored" or "restoring" or "unknown_effect");

    private Ui Body => Grid(
        [GridLength.Star(), GridLength.Auto],
        [GridLength.Auto],
        [
            Text(Status)
                .FontSize(13f).Foreground(LauncherTheme.Ink)
                .VAlign(VerticalAlignment.Center)
                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                .Automation(new()
                {
                    Name = Status,
                    AutomationId = "ai.repair.status",
                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                })
                .Cell(0, 0),
            LauncherControls.OutlineCommand(Resources.AiRepairUndo, () => _ = owner.State.Chats.RestoreRepairAsync(owner.Conversation))
                .Style(LauncherButtons.AiTextAction)
                .Enabled(CanRestore)
                .Visible(Trial?.AfterSettings is not null)
                .Automation(new()
                {
                    Name = Resources.AiRepairUndo,
                    AutomationId = "ai.repair.restore"
                })
                .Cell(1, 0),
        ])
        .ColumnSpacing(12f);
}