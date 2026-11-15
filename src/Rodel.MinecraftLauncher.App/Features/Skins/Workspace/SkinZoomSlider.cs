// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Graphics;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Controls the active main viewport, retaining native slider range and keyboard behavior.</summary>
internal sealed partial class SkinZoomSlider(SkinEditorState editor) : Control
{
    private readonly SkinBrushSliderFace _face = new(() => (float)((editor.Zoom - editor.MinimumZoom) / (editor.MaximumZoom - editor.MinimumZoom)));
    private static SliderPalette Invisible => new(
        new(Colors.Transparent, Colors.Transparent, Colors.Transparent, Colors.Transparent),
        new(Colors.Transparent, Colors.Transparent, Colors.Transparent, Colors.Transparent),
        new(Colors.Transparent, Colors.Transparent, Colors.Transparent, Colors.Transparent),
        Colors.Transparent,
        Colors.Transparent);

    private Ui Body => Overlay([
        _face,
        Slider(editor.Zoom, value => editor.SetZoom?.Invoke(value))
            .Minimum(editor.MinimumZoom).Maximum(editor.MaximumZoom).SmallChange(5).LargeChange(25)
            .Palette(Invisible)
            .Automation(new()
            {
                Name = Resources.SkinZoom,
                AutomationId = "skin.footer.zoom"
            }),
    ])
        .Height(28f);
}