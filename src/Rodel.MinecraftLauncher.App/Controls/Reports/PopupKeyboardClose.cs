// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Input;

namespace Rodel.MinecraftLauncher.App.Controls.Reports;

/// <summary>Routes Escape to the owning popup's close policy after child menus have had first refusal.</summary>
internal static class PopupKeyboardClose
{
    internal static IDisposable Bind(Window window, Action close, Func<bool> canClose) => window.Commands.Bind(new KeyChord(Key.Escape), new Command("popup.close", close, canClose), KeyScope.When(KeyContexts.Popup));
}