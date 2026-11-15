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

internal sealed partial class AiTurnView : Control
{
    private readonly AiPage owner;
    private readonly AiUserPromptCard _question;
    private readonly AiPathApprovalCard _pathApproval;

    internal AiTurnView(AiPage owner, AiUiTurn initial)
    {
        this.owner = owner;
        Value = initial;
        _question = new(() => Value.UserAnswer, () => owner.Revision);
        _pathApproval = new(() => Value.Approval, () => owner.Revision);
    }

    private partial AiUiTurn Turn { get; set; }

    // Receive the owner's revision as an explicit input: mutable turn snapshots can keep the same object identity.
    // Reading and incrementing this control's own revision inside the pooling effect would create a feedback loop.
    internal void Update(AiUiTurn value, int sourceRevision)
    {
        Turn = value;
        SourceRevision = sourceRevision;
    }

    private partial int SourceRevision { get; set; }
    private partial int Revision { get; set; }
    private AiUiTurn Value
    {
        get
        {
            _ = owner.Revision;
            _ = SourceRevision;
            _ = Revision;
            return Turn ?? field;
        }
    }

    private bool QuestionPending => Value.UserAnswer is { } answer && !Value.Parts.Any(part => part.Kind == AiTranscriptPartKind.Question && part.PromptId == answer.Prompt.Id && part.AnswerStatus.Length > 0);
    private AiTranscriptPart[] Parts
    {
        get
        {
            var previous = owner.Conversation.Turns.TakeWhile(turn => turn.Id != Value.Id).SelectMany(turn => turn.Parts).Where(part => part.Kind == AiTranscriptPartKind.Notice).Select(part => part.Text).ToHashSet(StringComparer.Ordinal);
            return [.. Value.Parts.Where(part => (part.Kind != AiTranscriptPartKind.Notice || previous.Add(part.Text)) && !(QuestionPending && part.Kind == AiTranscriptPartKind.Question && part.PromptId == Value.UserAnswer?.Prompt.Id))];
        }
    }

    private string Error => AiTranscriptContent.ErrorText(Value.Record.Error);
    private string ErrorCode => (Value.Record.Error as JsonObject)?["code"]?.ToString() ?? string.Empty;
    private string Status => ErrorCode == "AI_RESPONSE_INTERRUPTED" ? Resources.AiResponseInterruptedSummary : Value.Record.Status switch
    {
        "completed" => string.Empty,
        "failed" => Resources.AiFailed,
        "canceled" => Resources.AiCanceled,
        "interrupted" => Resources.AiInterrupted,
        "incomplete" => Resources.AiIncomplete,
        "refused" => Resources.AiRefused,
        "awaiting_confirmation" => string.Empty,
        "awaiting_user" => string.Empty,
        "awaiting_access" => Resources.AiAwaitingAccess,
        "queued" => Resources.AiChatQueued,
        _ => string.Empty,
    };
    private AiUiContextChange[] ContextChanges => [.. owner.Conversation.ContextChanges.Where(change => change.AfterTurnId == Value.Id)];

    private string ContextLabel(AiUiContextChange change) => change.Kind == "instance" ? string.Format(Resources.AiInstanceChanged, owner.Home.State.Instances.FirstOrDefault(item => item.Id == change.Value)?.DisplayName ?? change.Value ?? Resources.AiNoInstance) : string.Format(Resources.AiModelChanged, owner.State.Configuration.Providers.SelectMany(provider => provider.Models).FirstOrDefault(model => model.Id == change.Value)?.Name ?? change.Value);
    private Action AccessAction => owner.OpenAccess;
    private Action<string, Action> ChangeLayout => owner.ChangeTranscriptLayout;
    private string DetailsLabel => Value.Details ? Resources.AiHideDetails : Resources.AiErrorDetails;
    private string EffectWarning => owner.State.Chats.EffectCheckFailed(Value.Id)
        ? Resources.AiEffectsVerificationWarning
        : AiTurnObservation.HasEffectProblem(Value.Record) ? Resources.AiEffectsUnknownWarning : string.Empty;
    private static ButtonStyle ActionStyle => LauncherButtons.AiTextAction.Padding(6f, 4f);

    private Ui Body => Column([
        Border(Column([
                Text(Resources.AiYou)
                    .FontSize(11f).FontWeight(500).Foreground(LauncherTheme.Muted),
                SelectableText(Text(Value.Record.Prompt ?? string.Empty)
                        .Foreground(LauncherTheme.Ink).FontSize(14f)
                        .Wrap())
                    .Automation(new() { AutomationId = "ai.prompt." + Value.Id }),
                new AiReferenceImages(Value.Record.Images ?? [], id => owner.State.Service.Images.ReadBytes(id)).Visible(Value.Record.Images is { Length: > 0 }),
            ])
                .Spacing(8f))
            .Padding(12f)
            .Background(LauncherTheme.AiUserMessageBackground)
            .BorderBrush(LauncherTheme.Edge)
            .BorderThickness(LauncherTheme.HighContrast ? 1f : 0f)
            .Margin(new Thickness(-12f, 0f, -12f, 0f))
            .Visible(Value.Record.RetryOf is null),
        Column([
            ItemsRepeater(
                Parts,
                static part => part.Id,
                part => new AiTranscriptPartView(Value, part, ChangeLayout),
                (view, part) => view.Update(Value, part, ChangeLayout),
                new StackLayout { Spacing = 14f }),
            _question.Visible(QuestionPending),
            _pathApproval.Visible(Value.Approval is { IsActive: true, PathAccess: not null }),
            Wrap([
                Row([
                    LauncherIcons.Line("error", 14f, LauncherTheme.Error)
                        .Visible(Error.Length > 0),
                    Text(Status)
                        .FontSize(12f).Foreground(Error.Length > 0 ? LauncherTheme.Error : LauncherTheme.Muted)
                        .Wrap()
                        .Automation(new() { AutomationId = "ai.status." + Value.Id }),
                ])
                    .Spacing(6f)
                    .VAlign(VerticalAlignment.Center)
                    .Visible(Status.Length > 0),
                LauncherControls.OutlineCommand(Text(Resources.AiConfigureAccess)
                        .FontSize(11f), AccessAction)
                    .Style(ActionStyle)
                    .Visible(Value.Record.Status == "awaiting_access"),
                LauncherControls.OutlineCommand(Text(Resources.AiSendNow)
                        .FontSize(11f), () => owner.RunQueued(Value))
                    .Style(ActionStyle)
                    .Visible(Value.Record.Status == "queued")
                    .Enabled(!owner.Busy)
                    .Automation(new()
                    {
                        Name = Resources.AiSendNow,
                        AutomationId = "ai.queue.now." + Value.Id
                    }),
                LauncherControls.OutlineCommand(Text(Resources.AiCancelQueued)
                        .FontSize(11f), () => _ = owner.State.RunAsync(async () => await owner.State.Service.CancelTurnAsync(owner.Conversation.Id, Value.Id)))
                    .Style(ActionStyle)
                    .Visible(Value.Record.Status == "queued")
                    .Enabled(!owner.Busy)
                    .Automation(new()
                    {
                        Name = Resources.AiCancelQueued,
                        AutomationId = "ai.queue.cancel." + Value.Id
                    }),
                LauncherControls.OutlineCommand(Text(DetailsLabel)
                        .FontSize(11f), () => owner.ChangeTranscriptLayout("ai.details." + Value.Id, () =>
                        {
                            Value.Details = !Value.Details;
                            Revision++;
                        }))
                    .Style(ActionStyle)
                    .Visible(Error.Length > 0)
                    .Automation(new() { AutomationId = "ai.details." + Value.Id }),
                LauncherControls.OutlineCommand(Text(Resources.AiResume)
                        .FontSize(11f), () => owner.Send(Value))
                    .Style(ActionStyle)
                    .Automation(new()
                    {
                        AutomationId = "ai.resume." + Value.Id,
                        Name = Resources.AiResume
                    })
                    .Visible(AiChatStates.IsTerminal(Value.Record.Status) && Value.Record.Status != "completed" && Value.Record.UnknownEffects is not { Count: > 0 })
                    .Enabled(!owner.State.Busy && !owner.State.Conversations.Any(item => item.Active)),
            ])
                .ItemSpacing(8f)
                .LineSpacing(4f),
            Text(EffectWarning)
                .FontSize(12f).Foreground(LauncherTheme.Warning)
                .Wrap()
                .Visible(EffectWarning.Length > 0)
                .Automation(new()
                {
                    Name = EffectWarning,
                    AutomationId = "ai.effects.warning." + Value.Id
                }),
            Border(SelectableText(Text(Error)
                        .FontSize(12f).Foreground(LauncherTheme.Ink)
                        .Wrap())
                    .Automation(new() { AutomationId = "ai.error.original." + Value.Id }))
                .Padding(14f)
                .Background(LauncherTheme.ControlBackground)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(1f)
                .Visible(Value.Details && Error.Length > 0),
        ])
            .Spacing(10f),
        ItemsRepeater(
            ContextChanges,
            static change => change.Id,
            change => new AiContextDivider(change.Id, ContextLabel(change)),
            (view, change) => view.Update(ContextLabel(change)),
            new StackLayout
            {
                EstimatedItemExtent = 20f,
                Spacing = 8f
            }),
    ])
        .Spacing(22f);
}