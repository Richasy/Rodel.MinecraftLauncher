// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Keeps UV navigation independent while routing edits through the editor's shared input owner.</summary>
internal sealed partial class SkinUvPanel(SkinEditorState editor) : Control
{
    private readonly Rendering.SkinViewport _viewport = new(editor, atlas: true, inset: true);

    private Ui Body => Border(Grid(
            [GridLength.Star()],
            [GridLength.Auto, GridLength.Star(), GridLength.Pixels(8f)],
            [
                Overlay([
                    new LauncherMaterialSurface(LauncherMaterials.Acrylic),
                    Grid(
                    [GridLength.Star(), GridLength.Auto, GridLength.Auto],
                    [GridLength.Auto],
                    [
                        Text(Resources.SkinUv)
                            .FontSize(11f)
                            .VAlign(VerticalAlignment.Center)
                            .Cell(0, 0),
                        Button(
                            Row([
                                LauncherIcons.Line("refresh", 14f)
                            ]),
                            _viewport.Reset)
                            .Style(SkinEditorChrome.QuietIconCommand)
                            .Enabled(editor.HasProject)
                            .Width(24f).Height(24f)
                            .ToolTip(Resources.SkinResetUv)
                            .Automation(new()
                            {
                                Name = Resources.SkinResetUv,
                                AutomationId = "skin.uv.reset"
                            })
                            .Cell(1, 0),
                        Button(
                            Row([
                                LauncherIcons.Line("close", 14f)
                            ]),
                            () => editor.UvVisible = false)
                            .Style(SkinEditorChrome.QuietIconCommand)
                            .Width(24f).Height(24f)
                            .ToolTip(Resources.SkinCloseUv)
                            .Automation(new()
                            {
                                Name = Resources.SkinCloseUv,
                                AutomationId = "skin.uv.close"
                            })
                            .Cell(2, 0),
                    ]).Padding(new Thickness(8f, 2f, 8f, 0f)),
                ])
                    .Cell(0, 0),
                Grid(
                    [GridLength.Pixels(8f), GridLength.Star(), GridLength.Pixels(8f)],
                    [GridLength.Star()],
                    [
                        new LauncherMaterialSurface(LauncherMaterials.Acrylic).Cell(0, 0),
                        _viewport.Visible(editor.HasProject).Cell(1, 0),
                        new LauncherMaterialSurface(LauncherMaterials.Acrylic).Cell(2, 0),
                    ])
                    .Cell(0, 1),
                new LauncherMaterialSurface(LauncherMaterials.Acrylic).Cell(0, 2),
            ]))
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(1f)
        .Width(editor.UvWidth).Height(editor.UvHeight);
}