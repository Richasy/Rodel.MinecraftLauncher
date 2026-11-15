// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Markdown;
using Sprout.Reconcile;
using Sprout.SyntaxHighlighting;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Displays model thinking, responses, tool exchanges and meaningful notices as separate content blocks.</summary>
internal sealed partial class AiTranscriptPartView : Control
{
    private AiUiTurn _turn;
    private AiTranscriptPart _initial;
    private Action<string, Action> _changeLayout;
    private bool _responseChrome;

    internal AiTranscriptPartView(
        AiUiTurn turn,
        AiTranscriptPart initial,
        Action<string, Action> changeLayout,
        bool responseChrome = true)
    {
        _turn = turn;
        _initial = initial;
        _changeLayout = changeLayout;
        _responseChrome = responseChrome;
        Expanded = turn.ExpandedParts.Contains(initial.Id);
    }

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is not AiTranscriptPartView next)
            return;
        _turn = next._turn;
        _initial = next._initial;
        _changeLayout = next._changeLayout;
        _responseChrome = next._responseChrome;
        Part = next.Value;
        Expanded = _turn.ExpandedParts.Contains(Value.Id);
    }

    private void Toggle() => _changeLayout("ai.content.toggle." + Prefix, () =>
        {
            var expanded = !Expanded;
            if (expanded)
                _turn.ExpandedParts.Add(Value.Id);
            else
                _turn.ExpandedParts.Remove(Value.Id);
            Expanded = expanded;
        });
    private partial AiTranscriptPart Part { get; set; }
    private partial bool Expanded { get; set; }
    private partial bool Hovered { get; set; }
    private partial bool Focused { get; set; }

    internal void Update(AiUiTurn turn, AiTranscriptPart part, Action<string, Action> changeLayout)
    {
        _turn = turn;
        _changeLayout = changeLayout;
        Part = part;
        Expanded = turn.ExpandedParts.Contains(part.Id);
    }

    internal AiTranscriptPart Value => Part ?? _initial;
    internal bool Thinking => Value.Kind == AiTranscriptPartKind.Thinking;
    internal string Prefix => _turn.Id + "." + Value.Id;
    private string Title => Thinking ? Resources.AiThinking : string.Format(Resources.AiToolCallTitle, Value.Name);
    private string ToolStatus => !Value.Complete ? Resources.AiWorking : Value.Status switch
    {
        "succeeded" or "completed" => Resources.AiToolCompleted,
        "canceled" => Resources.AiCanceled,
        "failed" or "refused" or "interrupted" => Resources.AiFailed,
        _ => Resources.AiToolResponseUnavailable,
    };

    private Ui Body => HoverArea(
        Column([
            Column([
                Grid(
                    [GridLength.Star(), GridLength.Auto],
                    [GridLength.Auto],
                    [
                        Text(Resources.AiResponse)
                            .FontSize(11f).Foreground(LauncherTheme.Muted)
                            .VAlign(VerticalAlignment.Center)
                            .Cell(0, 0),
                        Opacity(LauncherControls.OutlineCommand(
                                Row([
                                    LauncherIcons.Line("copy", 14f)
                                ]),
                                () => Sprout.Host.Clipboard.SetText(Value.Text))
                                .Style(LauncherButtons.PopupCloseButton)
                                .Width(26f).Height(26f)
                                .ToolTip(Resources.AiCopyAnswer)
                                .OnGotFocus(_ => Focused = true)
                                .OnLostFocus(() => Focused = false)
                                .Automation(new() { Name = Resources.AiCopyAnswer }))
                            .Opacity(Hovered || Focused ? 1f : 0f)
                            .Cell(1, 0),
                    ])
                    .Visible(_responseChrome),
                When(Value.Kind == AiTranscriptPartKind.Response,
                    new AiMarkdownView(Value.Text, AiMarkdownView.GetStyle(false), LauncherTheme.ControlBackground, LauncherTheme.Ink, !AiChatStates.IsTerminal(_turn.Record.Status), "ai.response." + Prefix)),
            ])
                .Spacing(8f)
                .Visible(Value.Kind == AiTranscriptPartKind.Response),
            Border(Column([
                    new AiTranscriptExpander(
LauncherAppearance.Capture(),                        Title,
                        Thinking ? "spark" : "servers",
                        Thinking ? string.Empty : ToolStatus,
                        Expanded,
                        () => Toggle(),
                        "ai.content.toggle." + Prefix,
                        AiTranscriptExpander.Duration(Value.Elapsed)),
                    When(Expanded, new AiTranscriptDetails(
LauncherAppearance.Capture(),                            Value,
                            Thinking,
                            Prefix,
                            AiMarkdownView.GetStyle(true),
                            LauncherTheme.ControlBackground,
                            LauncherTheme.Ink)),
                ])
                    .Spacing(0f))
                .Background(LauncherTheme.ControlBackground)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(1f)
                .Visible(Value.Kind is AiTranscriptPartKind.Thinking or AiTranscriptPartKind.Tool),
            Border(Column([
                    Text(Resources.AiSpecialMessage)
                        .FontSize(11f).Foreground(LauncherTheme.Warning),
                    SelectableText(Text(Value.Text)
                            .FontSize(12f).Foreground(LauncherTheme.Ink)
                            .Wrap())
                        .Automation(new() { AutomationId = "ai.notice." + Prefix }),
                ])
                    .Spacing(6f))
                .Padding(12f)
                .Background(LauncherTheme.ControlBackground)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(1f)
                .Visible(Value.Kind == AiTranscriptPartKind.Notice),
            Border(Column([
                    Column([
                        Text(Resources.AiUserPromptQuestion)
                            .FontSize(11f).Foreground(LauncherTheme.Muted)
                            .Automation(new() { AutomationId = "ai.question.receipt." + Prefix }),
                        SelectableText(Text(Value.Text)
                                .FontSize(14f).Foreground(LauncherTheme.Ink)
                                .Wrap())
                            .Automation(new() { AutomationId = "ai.question.receipt.question." + Prefix }),
                    ])
                        .Spacing(6f),
                    Border(Column([
                            Text(Resources.AiUserPromptAnswer)
                                .FontSize(11f).Foreground(LauncherTheme.Muted),
                            SelectableText(Text(Value.Answer)
                                    .FontSize(14f).Foreground(LauncherTheme.Ink)
                                    .Wrap())
                                .Automation(new() { AutomationId = "ai.question.receipt.answer." + Prefix }),
                        ])
                            .Spacing(6f))
                        .Padding(10f, 8f)
                        .Background(LauncherTheme.AiUserMessageBackground)
                        .Visible(Value.AnswerStatus == "answered"),
                    Text(Resources.AiUserPromptSkipped)
                        .FontSize(12f).Foreground(LauncherTheme.Muted)
                        .Wrap()
                        .Visible(Value.AnswerStatus == "skipped"),
                    Text(ToolStatus)
                        .FontSize(12f).Foreground(LauncherTheme.Muted)
                        .Wrap()
                        .Visible(Value.AnswerStatus.Length == 0),
                    SelectableText(Text(Value.Error)
                            .FontSize(12f).Foreground(LauncherTheme.Error)
                            .Wrap())
                        .Visible(Value.Error.Length > 0),
                ])
                    .Spacing(10f))
                .Padding(12f)
                .Background(LauncherTheme.ControlBackground)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(1f)
                .Visible(Value.Kind == AiTranscriptPartKind.Question),
        ]),
        value => Hovered = value);
}

/// <summary>Creates expensive selectable Markdown/JSON only for an explicitly expanded content block.</summary>
[View]
internal readonly partial record struct AiTranscriptDetails(
    LauncherAppearance Theme,
    AiTranscriptPart Value,
    bool Thinking,
    string Prefix,
    MarkdownStyle Appearance,
    Color CodeSurface,
    Color Ink)
{
    /// <summary>Displays the complete original details without an estimated nested list.</summary>
    public Ui Body => Column([
        When(Thinking, new AiMarkdownView(Value.Text, Appearance, CodeSurface, Ink, !Value.Complete, "ai.thinking." + Prefix)),
        Text(Resources.AiToolArguments)
            .FontSize(11f).Foreground(Theme.Muted)
            .Visible(!Thinking && Value.Arguments.Length > 0),
        new AiJsonPreview(Theme,Value.Arguments, "ai.tool.arguments." + Prefix).Visible(!Thinking && Value.Arguments.Length > 0),
        Text(Resources.AiToolResponse)
            .FontSize(11f).Foreground(Theme.Muted)
            .Visible(!Thinking && Value.Complete),
        new AiJsonPreview(Theme,Value.Result, "ai.tool.result." + Prefix).Visible(!Thinking && Value.Result.Length > 0),
        Text(Resources.AiToolResponseUnavailable)
            .FontSize(12f).Foreground(Theme.Muted)
            .Wrap()
            .Visible(!Thinking && Value.Complete && Value.Result.Length == 0),
    ])
        .Spacing(10f)
        .Padding(12f);
}