// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Graphics;
using Sprout.Popups;
using System.Runtime.CompilerServices;

namespace Rodel.MinecraftLauncher.App.Controls;

/// <summary>One application modal per window; child menus remain owned by the active popup.</summary>
internal static class ModalPopup
{
    private sealed class State
    {
        internal Flyout? Current;
    }
    private static readonly ConditionalWeakTable<Window, State> Windows = [];

    internal static bool IsOpen(Window window) => Windows.GetValue(window, static _ => new State()).Current?.IsOpen == true;

    internal static bool TryShow(Window window, Flyout popup)
    {
        var state = Windows.GetValue(window, static _ => new State());
        if (state.Current?.IsOpen == true)
            return false;
        state.Current = popup;
        popup.Closed += Release;
        try
        {
            var size = window.ClientSize;
            popup.ShowAt(window, new Point(size.Width * .5f, size.Height * .5f));
        }
        catch
        {
            if (!popup.IsOpen)
                Release();
            throw;
        }

        return true;

        void Release()
        {
            popup.Closed -= Release;
            // A closed content factory can still retain full-resolution images or other draft data.
            if (ReferenceEquals(state.Current, popup))
                state.Current = null;
        }
    }
}