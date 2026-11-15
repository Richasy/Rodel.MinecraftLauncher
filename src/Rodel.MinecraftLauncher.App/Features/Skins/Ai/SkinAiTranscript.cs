// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Shows every saved turn and its streamed content in chronological order, not just the latest answer.</summary>
internal sealed partial class SkinAiTranscript(SkinEditorAiPanel owner) : Control
{
    private Ui Body => Column([
        SelectableText(Text(owner.Error)
                .FontSize(12f).Foreground(LauncherTheme.Error)
                .Wrap())
            .Visible(owner.Error.Length > 0)
            .Automation(new()
            {
                Name = owner.Error,
                AutomationId = "skin.ai.error"
            }),
        ItemsRepeater(
            owner.Turns,
            static turn => turn.Id,
            turn => new SkinAiTurnView(owner, turn),
            (view, turn) => view.Update(turn, owner.Revision),
            new AiTurnLayout(24f))
            .VirtualizationCache(0f),
    ])
        .Spacing(12f)
        .Padding(18f);
}