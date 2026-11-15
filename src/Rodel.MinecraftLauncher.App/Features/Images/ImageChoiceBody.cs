// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Images;

/// <summary>Retains the single scrolling image-selection body independently of the fixed actions.</summary>
internal sealed partial class ImageChoiceBody(ImageChoicePopup owner) : Control
{
    private const int PresetColumns = 7;
    private const float PresetSpacing = 8f;
    private readonly ImageCropSurface _surface = new(owner.Move);
    private readonly Action<int> _chooseSource = owner.ChooseSource;
    private float PresetEdge => (owner.Width - 32f - ((PresetColumns - 1) * PresetSpacing)) / PresetColumns;
    private ImageChoiceOption[] PresetOptions => [..owner._presets.Select(preset => new ImageChoiceOption(
LauncherAppearance.Capture(),                preset,
                owner.Selected == preset.Id,
                () => owner.Select(preset),
                PresetEdge,
                !owner.Saving))];

    /// <summary>Builds preview, source tabs and the selected source's controls from the owning draft.</summary>
    public Ui Body => Column([
        Row([
            Image(owner.Preview)
                .Stretch(ImageStretch.Uniform)
                .Width(64f).Height(64f),
            Column([
                Text(owner._name)
                    .FontSize(14f).FontWeight(600)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
                Text(owner.Label)
                    .FontSize(12f).Foreground(LauncherTheme.Muted),
            ])
                .Spacing(5f)
                .Weight(1f)
                .VAlign(VerticalAlignment.Center),
            LauncherControls.Command(Resources.ImageResetDefault, () => owner.Select(owner._presets[0]))
                .Style(LauncherButtons.FlatIconButton)
                .Visible(owner.Selected != owner._presets[0].Id)
                .Enabled(!owner.Saving)
                .Automation(new()
                {
                    Name = Resources.ImageResetDefault,
                    AutomationId = "image.reset",
                }),
        ])
            .Spacing(16f),
        new ImageSourceSegments(LauncherAppearance.Capture(),owner.Custom, _chooseSource, owner._avatar ? Resources.ImagePresetAvatarTab : Resources.ImagePresetTab, !owner.Saving),
        ItemsRepeater(PresetOptions, static option => option.Preset.Id, static option => option, new UniformGridLayout
            {
                ItemWidth = PresetEdge,
                ItemHeight = PresetEdge,
                ColumnSpacing = PresetSpacing,
                RowSpacing = PresetSpacing,
            })
            .KeyboardNavigation()
            .Visible(!owner.Custom),
        DropTarget(
            Column([
                LauncherControls.Command(
                    Column([
                        LauncherIcons.Line("folder", 28f)
                            .HAlign(HorizontalAlignment.Center),
                        Text(Resources.ImageChooseFile)
                            .FontSize(13f)
                            .HAlign(HorizontalAlignment.Center),
                        Text(Resources.ImageFileTypes)
                            .FontSize(12f).Foreground(LauncherTheme.Muted)
                            .HAlign(HorizontalAlignment.Center),
                    ])
                        .Spacing(12f),
                    () => _ = owner.BrowseAsync())
                    .Style(LauncherButtons.OutlineButton)
                    .Height(236f)
                    .Enabled(!owner.Loading && !owner.Saving)
                    .Visible(owner._crop is null && owner._original is not null)
                    .Automation(new()
                    {
                        Name = Resources.ImageChooseFile,
                        AutomationId = "image.file",
                    }),
                _surface.WithImage(owner.Preview)
                    .HAlign(HorizontalAlignment.Center)
                    .Visible(owner._crop is not null),
                Image(owner._initial)
                    .Stretch(ImageStretch.Uniform)
                    .Width(236f).Height(236f)
                    .HAlign(HorizontalAlignment.Center)
                    .Visible(owner._crop is null && owner._original is null),
                Row([
                    Text(Resources.ImageZoom)
                        .FontSize(12f).Foreground(LauncherTheme.Muted)
                        .VAlign(VerticalAlignment.Center)
                        .Visible(owner._crop is not null),
                    Slider(owner.Zoom, owner.SetZoom)
                        .Minimum(1).Maximum(4).SmallChange(.05).LargeChange(.25)
                        .Weight(1f)
                        .Enabled(!owner.Saving)
                        .Visible(owner._crop is not null)
                        .Automation(new()
                        {
                            Name = Resources.ImageZoom,
                            AutomationId = "image.zoom",
                        }),
                    LauncherControls.OutlineCommand(Resources.ImageRechoose, () => _ = owner.BrowseAsync())
                        .Enabled(!owner.Loading && !owner.Saving)
                        .Visible(owner._crop is not null || owner._original is null)
                        .Automation(new()
                        {
                            Name = Resources.ImageRechoose,
                            AutomationId = "image.rechoose",
                        }),
                ])
                    .Spacing(12f),
            ])
                .Spacing(12f)
                .Background(owner.Dropping ? LauncherTheme.ControlBackground : Colors.Transparent),
            owner.DropHandler)
            .Visible(owner.Custom)
            .Automation(new()
            {
                Name = Resources.ImageChooseFile,
                AutomationId = "image.drop",
            }),
        Text(Resources.ImageLoading)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Visible(owner.Loading),
        Text(owner.Error)
            .FontSize(12f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(owner.Error.Length > 0)
            .Automation(new()
            {
                Name = owner.Error,
                AutomationId = "image.error",
            }),
        Text(Resources.ImageAvatarScope)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(owner._avatar),
    ])
        .Spacing(16f)
        .Padding(16f);
}