// Copyright (c) Richasy. All rights reserved.

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

internal sealed record SkinAiToolEntry(AiTranscriptPart Part, int Number, SkinAiToolDetails? Details);

/// <summary>Explains where one call read or drew, including its layers, shapes, coordinates, colors and actual result.</summary>
internal sealed partial class SkinAiToolEntryView : Control
{
    private readonly SkinEditorAiPanel _owner;
    private AiUiTurn _turn;

    internal SkinAiToolEntryView(SkinEditorAiPanel owner, AiUiTurn turn, SkinAiToolEntry entry)
    {
        _owner = owner;
        _turn = turn;
        Value = entry;
    }

    private partial SkinAiToolEntry Entry { get; set; }
    private SkinAiToolEntry Value { get => Entry ?? field; set; }

    internal void Update(AiUiTurn turn, SkinAiToolEntry entry)
    {
        _turn = turn;
        Entry = entry;
    }

    private string Title => Value.Details?.Label is { } label && !string.IsNullOrWhiteSpace(label) ? label : SkinAiToolText.Name(Value.Part.Name);
    private string Description => Value.Details is { } details ? string.Join(Environment.NewLine, SkinAiToolText.Describe(Value.Part, details, _owner.LayerName)) : string.Empty;
    private bool Failed => Value.Part.Status is "failed" or "refused" or "interrupted";
    private string Status => !Value.Part.Complete ? Resources.AiWorking : Value.Part.Status switch
    {
        "succeeded" or "completed" => Resources.AiToolCompleted,
        "canceled" => Resources.AiCanceled,
        "failed" or "refused" or "interrupted" => Resources.AiFailed,
        _ => Resources.AiToolResponseUnavailable,
    };

    private Ui Body => new SkinAiToolStepContent(
LauncherAppearance.Capture(), Value.Number,
        Title,
        Description,
        Status,
        Value.Part.Error,
        "skin.ai.step." + _turn.Id + "." + Value.Part.Id,
        Failed ? LauncherTheme.Error : LauncherTheme.Muted);
}

[View]
internal readonly partial record struct SkinAiToolStepContent(
    LauncherAppearance Theme,
    int Number,
    string Title,
    string Description,
    string Status,
    string Error,
    string Id,
    Color StatusColor)
{
    /// <summary>Separates the recorded operation's intent from the tool's actual success, failure or unavailable result.</summary>
    public Ui Body => Column([
        Grid(
            [GridLength.Auto, GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                Text(Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".")
                    .FontSize(11f).Foreground(Theme.Muted)
                    .Cell(0, 0),
                Text(Title)
                    .FontSize(12f).Foreground(Theme.Ink)
                    .Wrap()
                    .Automation(new()
                    {
                        Name = Title,
                        AutomationId = Id
                    })
                    .Cell(1, 0),
                Text(Status)
                    .FontSize(11f).Foreground(StatusColor)
                    .Cell(2, 0),
            ])
            .ColumnSpacing(8f),
        SelectableText(Text(Description)
                .FontSize(11f).Foreground(Theme.Muted)
                .Wrap())
            .Visible(Description.Length > 0)
            .Automation(new()
            {
                Name = Description,
                AutomationId = Id + ".details"
            }),
        SelectableText(Text(Error)
                .FontSize(11f).Foreground(Theme.Error)
                .Wrap())
            .Visible(Error.Length > 0),
    ])
        .Spacing(6f);
}