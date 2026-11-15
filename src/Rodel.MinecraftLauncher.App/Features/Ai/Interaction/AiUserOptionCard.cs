// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

internal sealed partial class AiUserOptionCard(AiUserPromptCard owner, AiUserOption initial) : Control
{
    private partial AiUserOption Item { get; set; }
    private AiUserOption Option => Item ?? initial;
    private AiUserAnswer? Answer => owner.Answer;

    internal void Update(AiUserOption option) => Item = option;

    private Ui Body => Column([
        new LauncherCheckBox(
LauncherAppearance.Capture(),            Option.Label,
            Answer?.Selected.Contains(Option.Id) == true,
            () => Answer?.Toggle(Option.Id),
            owner.Enabled,
            "ai.question.option." + Option.Id).Visible(Answer?.Prompt.Question.Multiple == true),
        RadioButton(Text(Option.Label)
                .FontSize(14f).Foreground(LauncherTheme.Ink)
                .Wrap(), Answer?.Selected.Contains(Option.Id) == true, () => Answer?.Toggle(Option.Id), LauncherStyles.RadioChoice)
            .OuterSize(20f)
            .ContentSpacing(12f)
            .Enabled(owner.Enabled)
            .Visible(Answer?.Prompt.Question.Multiple != true)
            .Automation(new()
            {
                Name = Option.Label,
                AutomationId = "ai.question.option." + Option.Id
            }),
        Text(Option.Description ?? string.Empty)
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(!string.IsNullOrEmpty(Option.Description))
            .Margin(new Thickness(32f, 0f, 0f, 0f)),
    ])
        .Spacing(3f);
}