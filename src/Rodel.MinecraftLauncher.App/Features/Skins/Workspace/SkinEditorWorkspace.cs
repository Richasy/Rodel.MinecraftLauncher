// Copyright (c) Richasy. All rights reserved.

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

/// <summary>Composes two retained main views and an independently navigable UV editor over the same session.</summary>
internal sealed partial class SkinEditorWorkspace(SkinEditorState editor) : Control
{
    private readonly Rendering.SkinViewport _viewport = new(editor);
    private readonly Rendering.SkinViewport _flat = new(editor, atlas: true);
    private readonly SkinUvPanel _uv = new(editor);

    protected override IDisposable? OnMounted()
    {
        editor.UvBounds = () => editor.ShowUv ? _uv.Render.Bounds : null;
        return null;
    }

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Star()],
        [
            Grid(
                [GridLength.Auto, GridLength.Star(), GridLength.Auto, GridLength.Auto],
                [GridLength.Auto, GridLength.Pixels(1f)],
                [
                    new SkinViewSegments(editor.FlatView, index => editor.FlatView = index == 1, LauncherMaterials.SegmentedMaterial).Cell(0, 0),
                    ToggleButton(
                        Row([
                            LauncherIcons.Line("skin-grid", 14f)
                                .VAlign(VerticalAlignment.Center),
                            Text(Resources.SkinGrid)
                                .FontSize(11f)
                                .VAlign(VerticalAlignment.Center)
                        ])
                            .Spacing(5f),
                        editor.GridVisible,
                        SkinEditorChrome.TextTogglePalette)
                        .OnToggled(value => editor.GridVisible = value == true)
                        .Style(SkinEditorChrome.TextToggleStyle)
                        .Automation(new()
                        {
                            Name = Resources.SkinGrid,
                            AutomationId = "skin.grid"
                        })
                        .Cell(2, 0),
                    ToggleButton(
                        Row([
                            LauncherIcons.Line("skin-uv", 14f)
                                .VAlign(VerticalAlignment.Center),
                            Text(Resources.SkinUv)
                                .FontSize(11f)
                                .VAlign(VerticalAlignment.Center)
                        ])
                            .Spacing(5f),
                        editor.UvVisible,
                        SkinEditorChrome.TextTogglePalette)
                        .OnToggled(value => editor.UvVisible = value == true)
                        .Style(SkinEditorChrome.TextToggleStyle)
                        .Automation(new()
                        {
                            Name = Resources.SkinUv,
                            AutomationId = "skin.uv.toggle"
                        })
                        .Visible(!editor.FlatView)
                        .Cell(3, 0),
                    Rectangle(editor.GridVisible ? LauncherTheme.Ink : Colors.Transparent)
                        .Cell(2, 1),
                    Rectangle(editor.UvVisible ? LauncherTheme.Ink : Colors.Transparent)
                        .Visible(!editor.FlatView)
                        .Cell(3, 1),
                ])
                .ColumnSpacing(16f)
                .MinHeight(50f)
                .Padding(0f, 8f)
                .Margin(new Thickness(editor.Compact ? 14f : 20f, 0f, editor.Compact ? 14f : 20f, 0f))
                .VAlign(VerticalAlignment.Center)
                .Cell(0, 0),
            Border(Overlay([
                    _viewport.Visible(editor.HasProject && !editor.FlatView),
                    _flat.Visible(editor.HasProject && editor.FlatView),
                    PointerArea(Border(_uv), BlockPointer)
                        .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Bottom)
                        .Margin(new Thickness(16f, 16f, 16f, editor.UvBottom))
                        .Visible(editor.ShowUv),
                ]))
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(1f)
                .Background(LauncherTheme.HighContrast ? LauncherTheme.Background : LauncherTheme.ControlBackground.MultiplyAlpha(.22f))
                .Margin(new Thickness(editor.Compact ? 14f : 20f, 0f, editor.Compact ? 14f : 20f, 16f))
                .Cell(0, 1),
        ]);

    private static PointerEventResult BlockPointer(in PointerEvent _) => new(true, PointerCaptureRequest.None);
}

/// <summary>Uses native single-selection semantics for the retained main viewport pair.</summary>
[View]
internal readonly partial record struct SkinViewSegments(bool Flat, Action<int> Choose, LauncherSegmentedMaterial Material)
{
    /// <summary>Applies the shared material to the native view selector.</summary>
    public Ui Body => Column([
        LauncherControls.Segmented(new SkinViewFilter(Flat, Choose), Material),
    ]);
}

internal sealed partial class SkinViewFilter : Control
{
    internal SkinViewFilter(bool flat, Action<int> choose)
    {
        Flat = flat;
        Choose = choose;
    }

    private partial bool Flat { get; set; }
    private partial Action<int> Choose { get; set; }
    private static SegmentedItem[] Items => [new("3D")
        {
            Automation = new()
            {
                AutomationId = "skin.view.model"
            }
        }, new(Resources.SkinFlat)
        {
            Automation = new()
            {
                AutomationId = "skin.view.flat"
            }
        }, ];

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is SkinViewFilter other)
        {
            Flat = other.Flat;
            Choose = other.Choose;
        }
    }

    private Ui Body => Segmented(Items)
        .SelectedIndex(Flat ? 1 : 0)
        .OnSelectionChanged(index => Choose(index))
        .Style(LauncherStyles.Segmented)
        .MinHeight(32f).MinWidth(132f)
        .Automation(new() { AutomationId = "skin.view.switch" });
}