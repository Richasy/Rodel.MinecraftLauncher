// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Popups;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Exposes both creation paths directly beside the palette selector.</summary>
internal sealed partial class SkinPaletteCreateButton(SkinEditorState editor) : Control
{
    private MenuFlyout? _menu;

    private Ui Body => Row([
        Button(Row([LauncherIcons.Line("skin-add", 16f)]), () => ShowMenu())
            .Style(SkinEditorChrome.QuietIconCommand)
            .Width(28f).Height(28f)
            .Enabled(editor.Palettes.CanWrite && !editor.FileActive)
            .ToolTip(Resources.SkinPaletteNewMenu)
            .Automation(new() { Name = Resources.SkinPaletteNewMenu, AutomationId = "skin.palette.create" }),
    ]);

    private void ShowMenu()
    {
        _menu?.Hide();
        _menu = new MenuFlyout(
            new MenuFlyoutItem(Resources.SkinPaletteNew, () => DispatcherQueue.GetForCurrentThread().TryEnqueue(
                () => editor.Palettes.Show(SkinPaletteMode.New)))
            {
                Icon = LauncherIcons.Element("palette-new"),
                Automation = new() { AutomationId = "skin.palette.new" },
            },
            new MenuFlyoutItem(Resources.SkinPaletteFromImage, () => DispatcherQueue.GetForCurrentThread().TryEnqueue(
                () => _ = editor.Palettes.FromImageAsync()))
            {
                Icon = LauncherIcons.Element("image-picker"),
                Automation = new() { AutomationId = "skin.palette.from-image" },
            });
        _menu.Style(LauncherMenus.CommandMenu);
        if (Interaction is { } anchor)
            _menu.ShowAt(editor.Controller.Window, anchor);
    }

    protected override void OnDispose()
    {
        _menu?.Hide();
        base.OnDispose();
    }
}