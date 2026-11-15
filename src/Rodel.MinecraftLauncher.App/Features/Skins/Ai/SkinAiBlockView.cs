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

/// <summary>Shares the normal rich response renderer and one retained native expander for each intervening tool sequence.</summary>
internal sealed partial class SkinAiBlockView : Control
{
    private readonly SkinEditorAiPanel _owner;
    private readonly AiTranscriptPartView _rich;
    private readonly SkinAiToolGroup _group;
    private AiUiTurn _turn;

    internal SkinAiBlockView(SkinEditorAiPanel owner, AiUiTurn turn, SkinAiActivityBlock block)
    {
        _owner = owner;
        _turn = turn;
        Value = block;
        _rich = new(turn, block.Content ?? block.Entries[0], owner.ChangeTranscriptLayout, responseChrome: false);
        _group = new(owner, turn, block);
    }

    private partial SkinAiActivityBlock Block { get; set; }
    private SkinAiActivityBlock Value { get => Block ?? field; set; }

    internal void Update(AiUiTurn turn, SkinAiActivityBlock block)
    {
        _turn = turn;
        Block = block;
        _group.Update(turn, block);
        _rich.Update(turn, block.Content ?? block.Entries[0], _owner.ChangeTranscriptLayout);
    }

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is SkinAiBlockView other)
        {
            Value = other.Value;
            Update(other._turn, other.Value);
        }
    }

    private Ui Body => When(Value.Content is null, new SkinAiGroupContent(_group), new SkinAiRichContent(_rich));
}

[View]
internal readonly partial record struct SkinAiGroupContent(SkinAiToolGroup Content)
{
    /// <summary>Mounts one retained tool sequence without showing per-invocation expanders.</summary>
    public Ui Body => Content;
}

[View]
internal readonly partial record struct SkinAiRichContent(AiTranscriptPartView Content)
{
    /// <summary>Mounts the retained rich content only for messages, thinking or actual notices.</summary>
    public Ui Body => Content;
}