// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Skins.Rendering;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Owns one frozen current composite and a manually closed walking preview.</summary>
internal sealed partial class SkinAnimationDialog : Control
{
    private readonly SkinEditorState _editor;
    private readonly Action _close;
    private readonly SkinDialogPreview _preview;
    private partial string Error { get; set; }

    internal SkinAnimationDialog(SkinEditorState editor, SkinRenderSnapshot snapshot, Action close)
    {
        var companion = editor.Controller.Outfit.Reference;
        _editor = editor;
        _close = close;
        Error = string.Empty;
        _preview = new(
            editor.Controller.Window,
            () => snapshot,
            () => false,
            () => false,
            error => Error = error,
            "skin.preview.viewport",
            animated: true,
            companion: () => companion);
    }

    private Ui Body => Overlay([
        _preview,
        Text(Error)
            .FontSize(13f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Margin(new Thickness(16f))
            .VAlign(VerticalAlignment.Bottom)
            .Visible(Error.Length > 0)
            .Automation(new() { AutomationId = "skin.preview.error" }),
        Button(
            Row([
                LauncherIcons.Line("close", 16f)
            ]),
            _close)
            .Style(SkinEditorChrome.QuietIconCommand)
            .Width(32f).Height(32f)
            .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Top)
            .Margin(new Thickness(12f))
            .ToolTip(Resources.SkinCloseDialog)
            .Automation(new()
            {
                Name = Resources.SkinCloseDialog,
                AutomationId = "skin.preview.close"
            }),
    ])
        .Width(Math.Min(760f, _editor.Width - 96f)).Height(Math.Min(640f, _editor.Height - 120f));

    protected override IDisposable? OnMounted() => PopupKeyboardClose.Bind(_editor.Controller.Window, _close, () => true);
}