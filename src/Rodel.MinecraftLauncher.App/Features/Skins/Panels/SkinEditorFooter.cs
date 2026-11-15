// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Shows durable operation status alongside the current paint target and document format.</summary>
internal sealed partial class SkinEditorFooter(SkinEditorState editor) : Control
{
    private readonly SkinZoomSlider _zoom = new(editor);
    private string Status => editor.Status.Length > 0 ? editor.Status : Resources.SkinSaved;
    private string Layer => string.Format(Resources.SkinFooterLayer, editor.Layers.FirstOrDefault(layer => layer.Id == editor.ActiveLayerId)?.Name ?? string.Empty);
    private string Format => string.Format(Resources.SkinWorkDetails, editor.IsCape
            ? Resources.SkinCape
            : editor.DocumentModel == SkinModel.Slim ? Resources.SkinSlimModel : Resources.SkinClassicModel, editor.TextureSize, editor.TextureHeight);

    private Ui Body => Border(Grid(
            [GridLength.Star(), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto],
            [GridLength.Star()],
            [
                Text(Status)
                    .FontSize(10f).Foreground(LauncherTheme.Muted)
                    .VAlign(VerticalAlignment.Center)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                    .Automation(new()
                    {
                        Name = Status,
                        AutomationId = "skin.footer.status"
                    })
                    .Cell(0, 0),
                Text(Layer)
                    .MaxWidth(200f)
                    .FontSize(10f).Foreground(LauncherTheme.Muted)
                    .VAlign(VerticalAlignment.Center)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                    .Automation(new()
                    {
                        Name = Layer,
                        AutomationId = "skin.footer.layer"
                    })
                    .Cell(1, 0),
                Text(editor.OuterSurface ? Resources.SkinOuterSurface : Resources.SkinBaseSurface)
                    .FontSize(10f).Foreground(LauncherTheme.Muted)
                    .VAlign(VerticalAlignment.Center)
                    .Automation(new()
                    {
                        Name = editor.OuterSurface ? Resources.SkinOuterSurface : Resources.SkinBaseSurface,
                        AutomationId = "skin.footer.surface"
                    })
                    .Cell(2, 0),
                Text(Format)
                    .FontSize(10f).Foreground(LauncherTheme.Muted)
                    .VAlign(VerticalAlignment.Center)
                    .Automation(new()
                    {
                        Name = Format,
                        AutomationId = "skin.footer.format"
                    })
                    .Cell(3, 0),
                Row([
                    _zoom.Width(112f),
                    Text($"{editor.Zoom:0}%")
                        .FontSize(10f).Foreground(LauncherTheme.Muted)
                        .VAlign(VerticalAlignment.Center),
                ])
                    .Spacing(4f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(4, 0),
            ])
            .ColumnSpacing(16f))
        .Padding(17f, 4f, 17f, 4f)
        .MinHeight(32f)
        .Background(LauncherTheme.ControlBackground)
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(0f, 1f, 0f, 0f);
}