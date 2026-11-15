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

/// <summary>Preserves user messages, thinking, intermediate answers, tool activity and the actual completion state.</summary>
internal sealed partial class SkinAiTurnView(SkinEditorAiPanel owner, AiUiTurn initial) : Control
{
    private partial AiUiTurn Turn { get; set; }

    internal void Update(AiUiTurn turn, int sourceRevision)
    {
        Turn = turn;
        SourceRevision = sourceRevision;
    }

    private partial int SourceRevision { get; set; }
    private AiUiTurn Value
    {
        get
        {
            _ = owner.Revision;
            _ = SourceRevision;
            return Turn ?? initial;
        }
    }

    private string Error => owner.HasContinuation(Value) || Value.OutputLimited ? string.Empty : Value.Record.Error?["message"]?.ToString() ?? string.Empty;
    private string Status => owner.HasContinuation(Value)
        ? string.Empty
        : Value.OutputLimited ? Resources.SkinAiOutputLimit : Value.Record.Status switch
        {
            "completed" => string.Empty,
            "canceled" => Resources.AiCanceled,
            "incomplete" => Resources.AiIncomplete,
            "interrupted" => Resources.AiInterrupted,
            "refused" => Resources.AiRefused,
            "failed" => Resources.AiFailed,
            "queued" => Resources.AiChatQueued,
            _ => string.Empty,
        };
    private string EffectWarning => owner.EffectCheckFailed(Value)
        ? Resources.AiEffectsVerificationWarning
        : AiTurnObservation.HasEffectProblem(Value.Record) ? Resources.AiEffectsUnknownWarning : string.Empty;
    private bool HasFooter => Status.Length > 0 || Error.Length > 0 || owner.ShowRetry(Value) || EffectWarning.Length > 0;

    private Ui Body => Column([
        Border(Column([
                Text(Resources.AiYou)
                    .FontSize(11f).Foreground(LauncherTheme.Muted),
                SelectableText(Text(Value.Record.Prompt ?? string.Empty)
                        .FontSize(14f).Foreground(LauncherTheme.Ink)
                        .Wrap())
                    .Automation(new()
                    {
                        Name = Value.Record.Prompt ?? string.Empty,
                        AutomationId = "skin.ai.message." + Value.Id
                    }),
                new AiReferenceImages(Value.Record.Images ?? [], id => owner.ReadImage(id)).Visible(Value.Record.Images is { Length: > 0 }),
            ])
                .Spacing(6f))
            .Padding(12f)
            .Background(LauncherTheme.AiUserMessageBackground)
            .Visible(Value.Record.RetryOf is null),
        ItemsRepeater(
            SkinAiActivity.Group(Value.Parts),
            static block => block.Id,
            block => new SkinAiBlockView(owner, Value, block),
            (view, block) => view.Update(Value, block),
            new StackLayout { Spacing = 14f }),
        Column([
            Text(Status)
                .FontSize(12f).Foreground(Value.OutputLimited ? LauncherTheme.Warning : LauncherTheme.Muted)
                .Wrap()
                .Visible(Status.Length > 0)
                .Automation(new()
                {
                    Name = Status,
                    AutomationId = "skin.ai.status." + Value.Id
                }),
            SelectableText(Text(Error)
                    .FontSize(12f).Foreground(LauncherTheme.Error)
                    .Wrap())
                .Visible(Error.Length > 0),
            Row([
                LauncherControls.OutlineCommand(Resources.AiSendNow, () => owner.RunQueued(Value))
                    .Style(LauncherButtons.AiTextAction)
                    .Enabled(!owner.Busy)
                    .Automation(new()
                    {
                        Name = Resources.AiSendNow,
                        AutomationId = "skin.ai.queue.now." + Value.Id
                    }),
                LauncherControls.OutlineCommand(Resources.AiCancelQueued, () => owner.CancelQueued(Value))
                    .Style(LauncherButtons.AiTextAction)
                    .Enabled(!owner.Busy)
                    .Automation(new()
                    {
                        Name = Resources.AiCancelQueued,
                        AutomationId = "skin.ai.queue.cancel." + Value.Id
                    }),
            ])
                .Spacing(8f)
                .Visible(Value.Record.Status == "queued"),
            LauncherControls.OutlineCommand(Resources.AiResume, () => owner.Retry(Value))
                .Style(LauncherButtons.AiTextAction)
                .Visible(owner.ShowRetry(Value))
                .Enabled(owner.CanRetry(Value))
                .Automation(new()
                {
                    Name = Resources.AiResume,
                    AutomationId = "skin.ai.resume." + Value.Id
                }),
            Text(EffectWarning)
                .FontSize(12f).Foreground(LauncherTheme.Warning)
                .Wrap()
                .Visible(EffectWarning.Length > 0)
                .Automation(new()
                {
                    Name = EffectWarning,
                    AutomationId = "skin.ai.effects.warning." + Value.Id
                }),
        ])
            .Spacing(8f)
            .Visible(HasFooter),
    ])
        .Spacing(14f);
}