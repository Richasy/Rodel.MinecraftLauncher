// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using System.Numerics;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Opens fully loaded history at its end and follows later streamed content only while the player stays at the bottom.</summary>
internal sealed class AiTranscriptScroll : IDisposable
{
    private readonly ScrollView _view;
    private readonly Window _window;
    private readonly Func<string, InteractionNode?> _findAnchor;
    private AiUiConversation? _conversation;
    private bool _active, _restore, _moving;
    private readonly AiTranscriptFollow _follow = new();
    private (string Id, float Top, int Attempts)? _anchor;

    internal AiTranscriptScroll(ScrollView view, Window window, Func<string, InteractionNode?> findAnchor)
    {
        _view = view;
        _window = window;
        _findAnchor = findAnchor;
    }

    internal void Attach()
    {
        _window.LayoutUpdated += OnLayout;
        _view.ScrollChanged += Scrolled;
        _window.PointerWheelChanged += OnWheel;
        _window.PointerPressed += OnPointer;
    }

    internal void Activate(bool active, AiUiConversation? conversation)
    {
        if (_active == active && ReferenceEquals(_conversation, conversation))
            return;
        Changed(conversation);
        if (_active == active)
            return;
        if (!active)
            Remember();
        _active = active;
        _follow.Set(false);
        _anchor = null;
        if (active)
            _restore = true;
    }

    internal void Changed(AiUiConversation? conversation)
    {
        if (!ReferenceEquals(_conversation, conversation))
        {
            Remember();
            _conversation = conversation;
            _restore = true;
            _follow.Set(false);
            _anchor = null;
        }
    }

    internal void Restore()
    {
        _restore = true;
        _follow.Set(false);
    }

    internal void ScrollToEnd()
    {
        _anchor = null;
        MoveTo(_view.MaxScrollOffset);
        _restore = false;
        _follow.Set(true);
    }

    internal void ChangeLayout(string id, Action change)
    {
        _follow.Set(false);
        _anchor = _findAnchor(id) is { } target ? (id, target.TransformedHitRegion.Top, 0) : null;
        change();
    }

    private void Remember()
    {
        if (_active && !_restore && _conversation is { } conversation)
            conversation.TranscriptOffset = _view.Offset;
    }

    private void MoveTo(Vector2 offset)
    {
        _moving = true;
        try
        {
            _view.ScrollTo(offset, false);
        }
        finally
        {
            _moving = false;
        }

        _follow.Observe(_view.Offset.Y, _view.MaxScrollOffset.Y, automatic: true);
    }

    private void Scrolled(ScrollChangedArgs args)
    {
        _follow.Observe(args.Offset.Y, args.MaxScrollOffset.Y, _moving || !_active || _restore || _anchor is not null);
    }

    private bool Contains(Point point) => _active && _view.Interaction is { } node && node.TransformedHitRegion.Contains(point);

    private void OnWheel(PointerEvent packet)
    {
        if (packet.Scroll.Y > 0f && Contains(packet.Position))
        {
            _follow.Set(false);
            _anchor = null;
        }
    }

    private void OnPointer(PointerEvent packet)
    {
        if (Contains(packet.Position))
        {
            _follow.Set(false);
            _anchor = null;
        }
    }

    private void OnLayout()
    {
        if (!_active)
            return;
        if (_conversation?.HistoryLoading == true || _conversation?.HistoryFailed == true)
            return;
        if (_restore)
        {
            // Keep the saved position pending while a released transcript is still being rehydrated.
            // Remember must never persist the placeholder layout's clamped offset during a quick switch.
            if (_conversation?.Turns.Any(turn => AiChatStates.IsTerminal(turn.Record.Status) && !turn.ObservedTerminal) == true)
                return;
            _restore = false;
            _follow.Set(_conversation?.TranscriptOffset is null && _conversation?.Turns.Count > 0);
            MoveTo(_conversation?.TranscriptOffset ?? (_conversation?.Turns.Count > 0 ? _view.MaxScrollOffset : Vector2.Zero));
            return;
        }

        if (_anchor is { } anchor)
        {
            _follow.Set(false);
            _anchor = null;
            if (_findAnchor(anchor.Id) is { } target)
            {
                var delta = target.TransformedHitRegion.Top - anchor.Top;
                if (MathF.Abs(delta) > .5f && anchor.Attempts < 4)
                {
                    var before = _view.Offset;
                    MoveTo(before + new Vector2(0f, delta));
                    if (MathF.Abs(_view.Offset.Y - before.Y) > .1f)
                        _anchor = (anchor.Id, anchor.Top, anchor.Attempts + 1);
                }
            }

            return;
        }

        if (!_follow.Following)
            return;
        MoveTo(_view.MaxScrollOffset);
    }

    /// <summary>Remembers the current reading position and releases this window's layout callback.</summary>
    public void Dispose()
    {
        Remember();
        _window.LayoutUpdated -= OnLayout;
        _view.ScrollChanged -= Scrolled;
        _window.PointerWheelChanged -= OnWheel;
        _window.PointerPressed -= OnPointer;
    }
}