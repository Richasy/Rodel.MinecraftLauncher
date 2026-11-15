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

internal sealed partial class AiUserPromptCard : Control
{
    private readonly Func<AiUserAnswer?> _answer;
    private readonly Func<int> _revision;
    private readonly AiUserPromptBody _questions;

    internal AiUserPromptCard(Func<AiUserAnswer?> answer, Func<int> revision)
    {
        _answer = answer;
        _revision = revision;
        _questions = new(this);
    }

    internal AiUserAnswer? Answer
    {
        get
        {
            _ = _revision();
            return _answer();
        }
    }

    internal bool Enabled => Answer is { Working: false, Submitted: false };

    internal void Submit()
    {
        if (Answer is { CanSubmit: true } answer)
            _ = answer.SubmitAsync(false);
    }

    /// <summary>Presents an explicit answer surface without opening a modal or granting operation permissions.</summary>
    private Ui Body => Border(Column([
            Text(Resources.AiUserPromptTitle)
                .FontSize(12f).Foreground(LauncherTheme.Muted),
            _questions,
            Text(Answer?.Error ?? string.Empty)
                .FontSize(11f).Foreground(LauncherTheme.Error)
                .Wrap()
                .Visible(!string.IsNullOrEmpty(Answer?.Error)),
            Row([
                LauncherControls.Command(Resources.AiUserPromptSubmit, () => Submit())
                    .AsPrimary()
                    .Style(LauncherButtons.Accent)
                    .Enabled(Answer?.CanSubmit == true)
                    .Automation(new()
                    {
                        Name = Resources.AiUserPromptSubmit,
                        AutomationId = "ai.question.submit"
                    }),
                LauncherControls.OutlineCommand(Resources.AiUserPromptSkip, () =>
                    {
                        if (Answer is { } answer)
                            _ = answer.SubmitAsync(true);
                    })
                    .Enabled(Enabled)
                    .Automation(new()
                    {
                        Name = Resources.AiUserPromptSkip,
                        AutomationId = "ai.question.skip"
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

internal sealed partial class AiUserPromptBody(AiUserPromptCard owner) : Control
{
    private AiUserAnswer? Answer => owner.Answer;
    private AiUserOption[] Options => Answer?.Prompt.Question.Options ?? [];
    private bool Enabled => owner.Enabled;

    private Ui Body => Column([
        Text(Answer?.Prompt.Question.Question ?? string.Empty)
            .FontSize(14f).Foreground(LauncherTheme.Ink)
            .Wrap()
            .Automation(new()
            {
                Name = Answer?.Prompt.Question.Question ?? string.Empty,
                AutomationId = "ai.question.text"
            }),
        Text(Answer?.Prompt.Question.Multiple == true ? Resources.AiUserPromptMultiple : Resources.AiUserPromptSingle)
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Visible(Options.Length > 0),
        ItemsRepeater(
            Options,
            static option => option.Id,
            option => new AiUserOptionCard(owner, option),
            static (view, option) => view.Update(option),
            new StackLayout { Spacing = 14f }).KeyboardNavigation(),
        TextBox(Answer?.Text ?? string.Empty, value => Answer?.ChangeText(value))
            .SubmitOnEnter()
            .OnSubmitted(_ => owner.Submit())
            .ClearButton(false)
            .Placeholder(Resources.AiUserPromptCustom)
            .Style(LauncherStyles.Input)
            .Palette(LauncherStyles.InputPalette)
            .Enabled(Enabled)
            .Automation(new()
            {
                Name = Resources.AiUserPromptCustom,
                AutomationId = "ai.question.answer"
            }),
    ])
        .Spacing(8f);
}