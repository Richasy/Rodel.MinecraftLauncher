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

/// <summary>Collapses one consecutive tool sequence while retaining its actual steps and expand state.</summary>
internal sealed partial class SkinAiToolGroup : Control
{
    private readonly SkinEditorAiPanel _owner;
    private AiUiTurn _turn;

    internal SkinAiToolGroup(SkinEditorAiPanel owner, AiUiTurn turn, SkinAiActivityBlock block)
    {
        _owner = owner;
        _turn = turn;
        Value = block;
        Expanded = turn.ExpandedParts.Contains(block.Id);
    }

    private partial SkinAiActivityBlock Block { get; set; }
    private SkinAiActivityBlock Value { get => Block ?? field; set; }
    private partial bool Expanded { get; set; }
    private string HeaderId => "skin.ai.tools." + _turn.Id + "." + Value.Id;
    private AiTranscriptPart[] Tools => [.. Value.Entries.Where(part => part.Kind == AiTranscriptPartKind.Tool)];
    private string Header => Tools.Any(part => !part.Complete)
        ? Resources.SkinAiToolsWorking
        : Tools.Any(part => part.Status is not ("succeeded" or "completed")) ? string.Format(Resources.SkinAiToolsIssues, Tools.Length, Tools.Count(part => part.Status is not ("succeeded" or "completed"))) : string.Format(Resources.SkinAiToolsComplete, Tools.Length);
    private TimeSpan? Elapsed
    {
        get
        {
            var starts = Tools.Where(part => part.StartedUtc is not null).Select(part => part.StartedUtc!.Value).ToArray();
            var ends = Tools.Where(part => part.FinishedUtc is not null).Select(part => part.FinishedUtc!.Value).ToArray();
            if (starts.Length == 0)
                return null;
            var end = Tools.Any(part => !part.Complete)
                ? DateTimeOffset.UtcNow
                : ends.Length > 0 ? ends.Max() : (DateTimeOffset?)null;
            return end is { } finished && finished >= starts.Min() ? finished - starts.Min() : null;
        }
    }

    internal void Update(AiUiTurn turn, SkinAiActivityBlock block)
    {
        _turn = turn;
        Block = block;
        Expanded = turn.ExpandedParts.Contains(block.Id);
    }

    private void Toggle()
    {
        var expanded = !Expanded;
        _owner.ChangeTranscriptLayout(HeaderId, () =>
            {
                if (expanded)
                    _turn.ExpandedParts.Add(Value.Id);
                else
                    _turn.ExpandedParts.Remove(Value.Id);
                Expanded = expanded;
            });
    }

    private Ui Body => Border(Column([
            new AiTranscriptExpander(
LauncherAppearance.Capture(),                Header,
                "servers",
                string.Empty,
                Expanded,
                () => Toggle(),
                HeaderId,
                AiTranscriptExpander.Duration(Elapsed)),
            When(Expanded, new SkinAiToolStepsContent(_owner, _turn, Value)),
        ])
            .Spacing(0f))
        .Background(LauncherTheme.ControlBackground)
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(1f);
}

[View]
internal readonly partial record struct SkinAiToolStepsContent(SkinEditorAiPanel Owner, AiUiTurn Turn, SkinAiActivityBlock Block)
{
    /// <summary>Creates a fresh details subtree when reopened, with the current streamed step data.</summary>
    public Ui Body => new SkinAiToolSteps(Owner, Turn, Block);
}

/// <summary>Builds detailed step rows only while the shared disclosure has mounted its content.</summary>
internal sealed partial class SkinAiToolSteps(SkinEditorAiPanel owner, AiUiTurn initialTurn, SkinAiActivityBlock initialBlock) : Control
{
    private partial AiUiTurn Turn { get; set; }
    private partial SkinAiActivityBlock Block { get; set; }
    private AiUiTurn CurrentTurn => Turn ?? initialTurn;
    private SkinAiActivityBlock CurrentBlock => Block ?? initialBlock;

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is SkinAiToolSteps other)
        {
            Turn = other.CurrentTurn;
            Block = other.CurrentBlock;
        }
    }

    private SkinAiToolEntry[] Entries
    {
        get
        {
            var number = 0;
            return [.. CurrentBlock.Entries.Select(part => new SkinAiToolEntry(part, part.Kind == AiTranscriptPartKind.Tool ? ++number : 0, part.Kind == AiTranscriptPartKind.Tool ? SkinAiActivity.Read(part, CurrentTurn.Parts) : null))];
        }
    }

    private Ui Body => Border(ItemsRepeater(
            Entries,
            static entry => entry.Part.Id,
            entry => new SkinAiToolEntryView(owner, CurrentTurn, entry),
            (view, entry) => view.Update(CurrentTurn, entry),
            new StackLayout { Spacing = 14f }))
        .Padding(12f);
}