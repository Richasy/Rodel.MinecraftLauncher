// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Shows the fenced-code language and copies only that block's text, with feedback in the owning window.</summary>
internal sealed partial class AiCodeHeader(string language, string code) : Control
{
    private Window? _window;
    private string Language => string.IsNullOrWhiteSpace(language) ? Resources.AiCodePlainText : language;

    private Ui Body => Border(Grid(
            [GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                Text(Language)
                    .FontSize(11f).Foreground(LauncherTheme.Muted)
                    .Trim(Sprout.Graphics.TextTrimming.CharacterEllipsis)
                    .VAlign(VerticalAlignment.Center)
                    .Automation(new()
                    {
                        Name = Language,
                        AutomationId = "ai.code.language"
                    })
                    .Cell(0, 0),
                LauncherControls.OutlineCommand(
                    Row([
                        LauncherIcons.Line("copy", 14f)
                    ]),
                    Copy)
                    .Style(LauncherButtons.AiTextAction)
                    .Width(28f).Height(28f)
                    .ToolTip(Resources.AiCodeCopy)
                    .Automation(new()
                    {
                        Name = Resources.AiCodeCopy,
                        AutomationId = "ai.code.copy"
                    })
                    .Cell(1, 0),
            ])
            .ColumnSpacing(8f))
        .Padding(12f, 4f)
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(0f, 0f, 0f, 1f);

    private void Copy()
    {
        try
        {
            var copied = Sprout.Host.Clipboard.SetText(code);
            if (_window is not null)
            {
                if (copied) LauncherToast.Show(_window, Resources.AiCodeCopied);
                else LauncherToast.ShowTransientError(_window, Resources.LaunchErrorCopyFailed);
            }
        }
        catch
        {
            if (_window is not null)
                LauncherToast.ShowTransientError(_window, Resources.LaunchErrorCopyFailed);
        }
    }

    protected override IDisposable? OnMounted()
    {
        _window = ResolveWindow();
        return null;
    }
}