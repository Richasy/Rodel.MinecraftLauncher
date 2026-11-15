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

/// <summary>Keeps the lightweight panel navigation fixed above the scrollable property sections.</summary>
internal sealed partial class SkinEditorTabs(SkinEditorState editor) : Control
{
    // Reserve the selection indicator and bottom border inside the strip's minimum height.
    private float TabMinimumHeight => (editor.Height < 760f ? 42f : 50f) - 3f;

    private Ui Body => Border(LauncherControls.KeyboardGroup(Grid(
            [GridLength.Star(), GridLength.Star(1.5f), GridLength.Star()],
            [GridLength.Auto, GridLength.Pixels(2f)],
            [
                ToggleButton(Resources.SkinEdit, editor.Panel == 0, SkinEditorChrome.TextTogglePalette)
                    .OnToggled(_ => editor.Panel = 0)
                    .Style(SkinEditorChrome.TextToggleStyle)
                    .MinHeight(TabMinimumHeight)
                    .Automation(new() { AutomationId = "skin.tab.edit" })
                    .Cell(0, 0),
                ToggleButton(
                    Row([
                        LauncherIcons.Line("spark", 14f)
                            .VAlign(VerticalAlignment.Center),
                        Text(Resources.SkinAi)
                            .FontSize(12f)
                            .VAlign(VerticalAlignment.Center)
                    ])
                        .Spacing(6f),
                    editor.Panel == 1,
                    SkinEditorChrome.TextTogglePalette)
                    .OnToggled(_ => editor.Panel = 1)
                    .Style(SkinEditorChrome.TextToggleStyle)
                    .MinHeight(TabMinimumHeight)
                    .Automation(new()
                    {
                        Name = Resources.SkinAi,
                        AutomationId = "skin.tab.ai"
                    })
                    .Cell(1, 0),
                ToggleButton(Resources.SkinHistory, editor.Panel == 2, SkinEditorChrome.TextTogglePalette)
                    .OnToggled(_ => editor.Panel = 2)
                    .Style(SkinEditorChrome.TextToggleStyle)
                    .MinHeight(TabMinimumHeight)
                    .Automation(new() { AutomationId = "skin.tab.history" })
                    .Cell(2, 0),
                Rectangle(editor.Panel == 0 ? LauncherTheme.Ink : Colors.Transparent)
                    .Cell(0, 1),
                Rectangle(editor.Panel == 1 ? LauncherTheme.Ink : Colors.Transparent)
                    .Cell(1, 1),
                Rectangle(editor.Panel == 2 ? LauncherTheme.Ink : Colors.Transparent)
                    .Cell(2, 1),
            ])
            .ColumnSpacing(3f)))
        .Padding(14f, 0f, 14f, 0f)
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(0f, 0f, 0f, 1f);
}

[View]
internal readonly partial record struct SkinEditorPartButton(
    LauncherAppearance Theme,
    string Label,
    string Id,
    bool Selected,
    Action Choose,
    ToggleButtonPalette Palette,
    Sprout.Styling.ToggleButtonStyle Style)
{
    internal Action<bool?> Toggled
    {
        get
        {
            var choose = Choose;
            return _ => choose();
        }
    }

    /// <summary>Displays a body target spanning its complete property-grid column.</summary>
    public Ui Body => Row([
        LauncherControls.ToolToggle(Theme,
            Text(Label)
                .FontSize(11f),
            Selected,
            Toggled,
            true,
            Palette,
            Style)
            .MinHeight(28f)
            .Weight(1f)
            .Automation(new()
            {
                Name = Label,
                AutomationId = Id
            })
    ]);
}

[View]
internal readonly partial record struct SkinSurfaceSegments(SkinEditorState Editor, LauncherSegmentedMaterial Material)
{
    /// <summary>Uses the shared single-selection control for the target model surface.</summary>
    public Ui Body => Column([
        LauncherControls.Segmented(new SkinSurfaceFilter(Editor), Material)
    ]);
}

internal sealed partial class SkinSurfaceFilter(SkinEditorState editor) : Control
{
    private static SegmentedItem[] Items => [new(Resources.SkinBaseSurface)
        {
            Automation = new()
            {
                AutomationId = "skin.surface.base"
            }
        }, new(Resources.SkinOuterSurface)
        {
            Automation = new()
            {
                AutomationId = "skin.surface.outer"
            }
        }, ];

    private Ui Body => Segmented(Items)
        .SelectedIndex(editor.OuterSurface ? 1 : 0)
        .OnSelectionChanged(index => editor.OuterSurface = index == 1)
        .Style(LauncherStyles.Segmented)
        .MinHeight(28f).MinWidth(112f)
        .Automation(new() { AutomationId = "skin.surface.switch" });
}