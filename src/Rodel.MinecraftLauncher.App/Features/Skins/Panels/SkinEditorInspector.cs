// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Separates color, body targets and drawing layers into independently spaced property sections.</summary>
internal sealed partial class SkinEditorInspector(SkinEditorState editor) : Control
{
    private readonly SkinEditorLayerList _layers = new(editor);

    private Ui Body => Column([
        Column([
            new SkinOutfitPanel(editor).Visible(editor.IsCape),
            Border(new SkinEditorColorFields(editor))
                .Padding(editor.SectionPadding)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 0f, 0f, 1f),
            Border(Column([
                    Grid(
                        [GridLength.Star(), GridLength.Auto],
                        [GridLength.Auto],
                        [
                            Text(Resources.SkinBodyParts)
                                .FontSize(13f).FontWeight(600)
                                .VAlign(VerticalAlignment.Center)
                                .Cell(0, 0),
                            new LauncherCheckBox(
LauncherAppearance.Capture(),                                Resources.SkinShowAll,
                                editor.Parts == 63,
                                () => editor.Parts = editor.Parts == 63 ? 0 : 63,
                                true,
                                "skin.parts.all",
                                FontSize: 11f,
                                BoxSize: 14f,
                                Spacing: 6f).Cell(1, 0),
                        ]),
                    Grid(
                        [GridLength.Star(), GridLength.Auto],
                        [GridLength.Auto],
                        [
                            Text(Resources.SkinPaintPosition)
                                .FontSize(11f).Foreground(LauncherTheme.Muted)
                                .Wrap()
                                .VAlign(VerticalAlignment.Center)
                                .Cell(0, 0),
                            new SkinSurfaceSegments(editor, LauncherMaterials.SegmentedMaterial).Cell(1, 0),
                        ]),
                    LauncherControls.KeyboardGroup(Grid(
                        [GridLength.Star(), GridLength.Star()],
                        [GridLength.Auto, GridLength.Auto, GridLength.Auto],
                        [
                            new SkinEditorPartButton(LauncherAppearance.Capture(),
                                Resources.SkinHead,
                                "skin.part.0",
                                editor.IsPartVisible(0),
                                () => editor.TogglePart(0),
                                SkinEditorChrome.PropertyPalette,
                                SkinEditorChrome.PropertyStyle).Cell(0, 0),
                            new SkinEditorPartButton(LauncherAppearance.Capture(),
                                Resources.SkinTorso,
                                "skin.part.1",
                                editor.IsPartVisible(1),
                                () => editor.TogglePart(1),
                                SkinEditorChrome.PropertyPalette,
                                SkinEditorChrome.PropertyStyle).Cell(1, 0),
                            new SkinEditorPartButton(LauncherAppearance.Capture(),
                                Resources.SkinRightArm,
                                "skin.part.2",
                                editor.IsPartVisible(2),
                                () => editor.TogglePart(2),
                                SkinEditorChrome.PropertyPalette,
                                SkinEditorChrome.PropertyStyle).Cell(0, 1),
                            new SkinEditorPartButton(LauncherAppearance.Capture(),
                                Resources.SkinLeftArm,
                                "skin.part.3",
                                editor.IsPartVisible(3),
                                () => editor.TogglePart(3),
                                SkinEditorChrome.PropertyPalette,
                                SkinEditorChrome.PropertyStyle).Cell(1, 1),
                            new SkinEditorPartButton(LauncherAppearance.Capture(),
                                Resources.SkinRightLeg,
                                "skin.part.4",
                                editor.IsPartVisible(4),
                                () => editor.TogglePart(4),
                                SkinEditorChrome.PropertyPalette,
                                SkinEditorChrome.PropertyStyle).Cell(0, 2),
                            new SkinEditorPartButton(LauncherAppearance.Capture(),
                                Resources.SkinLeftLeg,
                                "skin.part.5",
                                editor.IsPartVisible(5),
                                () => editor.TogglePart(5),
                                SkinEditorChrome.PropertyPalette,
                                SkinEditorChrome.PropertyStyle).Cell(1, 2),
                        ])
                        .ColumnSpacing(5f).RowSpacing(5f)),
                    Grid(
                        [GridLength.Star(), GridLength.Auto],
                        [GridLength.Auto],
                        [
                            new LauncherCheckBox(
LauncherAppearance.Capture(),                                Resources.SkinShowOuter,
                                editor.OuterVisible,
                                () => editor.OuterVisible = !editor.OuterVisible,
                                true,
                                "skin.outer.visible",
                                FontSize: 11f,
                                BoxSize: 14f,
                                Spacing: 6f).Cell(0, 0),
                            Button(Resources.SkinCopyOpposite, () => _ = editor.Controller.CopyToOppositeAsync())
                                .Style(SkinEditorChrome.QuietCommand.Padding(8f, 4f))
                                .Enabled(editor.Controller.CanCopyToOpposite)
                                .Visible(editor.ShowCopyToOpposite)
                                .Automation(new()
                                {
                                    Name = Resources.SkinCopyOpposite,
                                    AutomationId = "skin.copy-opposite",
                                })
                                .Cell(1, 0),
                        ]),
                ])
                    .Spacing(12f))
                .Padding(editor.SectionPadding)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 0f, 0f, 1f)
                .Visible(!editor.IsCape),
            Border(Column([
                    Grid(
                        [GridLength.Star(), GridLength.Auto],
                        [GridLength.Auto],
                        [
                            Text(Resources.SkinLayers)
                                .FontSize(13f).FontWeight(600)
                                .VAlign(VerticalAlignment.Center)
                                .Cell(0, 0),
                            Row([
                                Button(
                                    Row([
                                        LauncherIcons.Line("skin-import", 15f)
                                    ]),
                                    editor.Controller.Files.ShowImportLayer)
                                    .Style(SkinEditorChrome.QuietIconCommand)
                                    .Enabled(editor.Controller.Files.CanExport)
                                    .Width(24f).Height(24f)
                                    .ToolTip(Resources.SkinLayerImportPng)
                                    .Automation(new()
                                    {
                                        Name = Resources.SkinLayerImportPng,
                                        AutomationId = "skin.layer.import"
                                    }),
                                Button(
                                    Row([
                                        LauncherIcons.Line("skin-add", 15f)
                                    ]),
                                    () => _ = editor.Controller.LayerActions.AddAsync())
                                    .Style(SkinEditorChrome.QuietIconCommand)
                                    .Enabled(editor.Controller.LayerActions.CanChange)
                                    .Width(24f).Height(24f)
                                    .ToolTip(Resources.SkinAddLayer)
                                    .Automation(new()
                                    {
                                        Name = Resources.SkinAddLayer,
                                        AutomationId = "skin.layer.add"
                                    }),
                            ])
                                .Spacing(4f)
                                .Cell(1, 0),
                        ]),
                    _layers.WithLayers(editor.Layers, editor.LayerThumbnails, editor.ActiveLayerId, editor.Controller.LayerActions.CanChange)
                        .Visible(editor.HasProject),
                    Text(Resources.SkinNoLayers)
                        .FontSize(11f).Foreground(LauncherTheme.Muted)
                        .Visible(!editor.HasProject),
                ])
                    .Spacing(12f))
                .Padding(editor.SectionPadding),
        ])
            .Visible(editor.Panel == 0),
    ]);
}