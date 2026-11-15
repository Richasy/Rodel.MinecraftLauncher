// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Input;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer.Rendering;

/// <summary>Tracks one left- or right-button orbit gesture, including release while scene content is unavailable.</summary>
internal sealed class SeedSceneOrbitGesture
{
    private Point _previous;
    private uint _pointer;
    private PointerButton _button;
    internal bool Active { get; private set; }
    internal void Cancel() => Active = false;
    internal PointerEventResult Handle(in PointerEvent e, bool enabled, out Point delta)
    {
        delta = default;
        if (Active && e.PointerId == _pointer && (e.Kind == PointerEventKind.Cancel || (e.Kind == PointerEventKind.Up && e.Button == _button)))
        {
            Active = false;
            return new(true, PointerCaptureRequest.Release);
        }

        if (!enabled) return PointerEventResult.Ignored;
        if (!Active && e.Kind == PointerEventKind.Down && e.Button is PointerButton.Primary or PointerButton.Secondary)
        {
            Active = true; _pointer = e.PointerId; _button = e.Button; _previous = e.RootPosition;
            return new(true, PointerCaptureRequest.Capture);
        }

        if (!Active || e.PointerId != _pointer || e.Kind != PointerEventKind.Move) return PointerEventResult.Ignored;
        delta = new(e.RootPosition.X - _previous.X, e.RootPosition.Y - _previous.Y);
        _previous = e.RootPosition;
        return new(true, PointerCaptureRequest.None);
    }
}