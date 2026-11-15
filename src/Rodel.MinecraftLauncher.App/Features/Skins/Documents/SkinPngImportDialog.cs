// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Skins;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Reviews a frozen PNG in model/atlas views with information and fixed actions alongside it.</summary>
internal sealed partial class SkinPngImportDialog : Control
{
    private readonly SkinEditorFiles _files;
    private readonly SkinEditorState _editor;
    private readonly SkinPngImport _input;
    private readonly ImageSource[] _previews;
    private readonly SkinRenderSnapshot[] _snapshots;
    private readonly Action _close;
    private readonly ScrollView _scroll;
    private readonly Rendering.SkinDialogPreview _modelPreview;

    internal SkinPngImportDialog(
        SkinEditorFiles files,
        SkinEditorState editor,
        SkinPngImport input,
        ImageSource[] previews,
        SkinRenderSnapshot[] snapshots,
        string path,
        Action close)
    {
        _files = files;
        _editor = editor;
        _input = input;
        _previews = previews;
        _snapshots = snapshots;
        _close = close;
        Model = !input.IsLegacy && (editor.Controller.Session?.Render().Model == SkinModel.Slim) ? 1 : 0;
        FileName = System.IO.Path.GetFileName(path);
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Error = string.Empty;
        _scroll = new(new SkinPngImportFields(this));
        _modelPreview = new(
            editor.Controller.Window,
            () => Snapshot,
            () => Flat,
            () => Working,
            error => Error = error,
            "skin.import.preview.model");
    }

    internal partial int Model { get; set; }
    internal partial bool Flat { get; set; }
    internal partial string Name { get; set; }
    internal partial string Error { get; set; }
    internal partial bool Working { get; set; }
    internal string FileName { get; }
    internal bool IsCape => _input.Model == SkinModel.Cape;
    internal bool IsCompanion => _files.IsCompanionImport;
    internal bool HasProject => !IsCompanion && _editor.HasProject && _editor.IsCape == IsCape;
    internal bool IsLegacy => _input.IsLegacy;
    internal bool Hd => _input.Size == 128;
    internal ImageSource Preview => _previews[Model];
    internal SkinRenderSnapshot Snapshot => _snapshots[Model];
    internal string SizeLabel => IsLegacy ? Resources.SkinPngLegacy : $"{_input.Size} × {_input.SourceHeight}";
    private float PreviewEdge => Math.Min(360f, _editor.Height - 232f);

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Star()],
        [
            Grid(
                [GridLength.Auto, GridLength.Auto, GridLength.Star(), GridLength.Auto],
                [GridLength.Auto],
                [
                    Text(IsCompanion
                            ? Resources.SkinImportCompanion
                            : IsCape ? Resources.SkinImportCape : Resources.SkinImport)
                        .FontSize(18f)
                        .VAlign(VerticalAlignment.Center)
                        .Cell(0, 0),
                    new SkinImportViewSegments(this, LauncherMaterials.SegmentedMaterial).Cell(1, 0),
                    Button(
                        Row([
                            LauncherIcons.Line("close", 16f)
                        ]),
                        _close)
                        .Style(SkinEditorChrome.QuietIconCommand)
                        .Width(32f).Height(32f)
                        .Enabled(!Working)
                        .ToolTip(Resources.SkinCloseDialog)
                        .Automation(new()
                        {
                            Name = Resources.SkinCloseDialog,
                            AutomationId = "skin.import.close"
                        })
                        .Cell(3, 0),
                ])
                .ColumnSpacing(20f)
                .Cell(0, 0),
            Grid(
                [GridLength.Pixels(360f), GridLength.Star()],
                [GridLength.Star()],
                [
                    Border(Overlay([
                            Image(Preview)
                                .Interpolation(ImageInterpolationMode.NearestNeighbor)
                                .Width(PreviewEdge).Height(PreviewEdge)
                                .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                                .Visible(Flat)
                                .Automation(new()
                                {
                                    Name = SizeLabel,
                                    AutomationId = "skin.import.preview"
                                }),
                            _modelPreview.Visible(!Flat),
                        ]))
                        .Background(LauncherTheme.ControlBackground)
                        .BorderBrush(LauncherTheme.Edge)
                        .BorderThickness(1f)
                        .Cell(0, 0),
                    Border(Grid(
                            [GridLength.Star()],
                            [GridLength.Star(), GridLength.Auto],
                            [
                                _scroll.Automation(new() { AutomationId = "skin.import.scroll" })
                                    .Cell(0, 0),
                                Border(Grid(
                                        [GridLength.Star()],
                                        [GridLength.Auto],
                                        [
                                            LauncherControls.Command(Resources.SkinImport, () => _ = ConfirmAsync())
                                                .Style(LauncherButtons.DialogPrimary)
                                                .MinHeight(40f)
                                                .Enabled(!Working && (HasProject || (Name.Trim()
                                                    .Length > 0)))
                                                .Automation(new() { AutomationId = "skin.import.confirm" })
                                                .Cell(0, 0),
                                        ])
                                        .ColumnSpacing(12f))
                                    .Padding(new Thickness(0f, 16f, 0f, 0f))
                                    .BorderBrush(LauncherTheme.Edge)
                                    .BorderThickness(0f, 1f, 0f, 0f)
                                    .Cell(0, 1),
                            ])
                            .RowSpacing(16f))
                        .Padding(16f)
                        .Background(LauncherTheme.ControlBackground)
                        .BorderBrush(LauncherTheme.Edge)
                        .BorderThickness(1f)
                        .Cell(1, 0),
                ])
                .ColumnSpacing(24f)
                .Cell(0, 1),
        ])
        .RowSpacing(20f)
        .Width(740f).Height(Math.Min(484f, _editor.Height - 120f));

    private async Task ConfirmAsync()
    {
        if (Working)
            return;
        Working = true;
        try
        {
            if (await _files.ConfirmImportAsync(IsCape
                    ? SkinModel.Cape
                    : Model == 1 ? SkinModel.Slim : SkinModel.Classic, Name))
            {
                _close();
            }
            else
            {
                Error = _files.LastError;
            }
        }
        finally
        {
            Working = false;
        }
    }

    protected override IDisposable? OnMounted() => PopupKeyboardClose.Bind(_editor.Controller.Window, _close, () => !Working);
}

internal sealed partial class SkinPngImportFields(SkinPngImportDialog dialog) : Control
{
    private Ui Body => Column([
        Text(dialog.FileName)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
        Column([
            Text(Resources.SkinTextureSize)
                .FontSize(12f),
            Text(dialog.SizeLabel)
                .FontSize(14f),
            Text(Resources.SkinHdUsage)
                .FontSize(11f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Visible(dialog.Hd && !dialog.IsCape),
        ])
            .Spacing(6f),
        Column([
            Text(Resources.SkinArmModel)
                .FontSize(12f),
            LauncherControls.Choice([Resources.SkinClassicModel, Resources.SkinSlimModel]).MinHeight(40f)
                .SelectedIndex(dialog.Model)
                .OnSelectionChanged(index => dialog.Model = index)
                .Enabled(!dialog.Working && !dialog.IsLegacy)
                .Automation(new()
                {
                    Name = Resources.SkinArmModel,
                    AutomationId = "skin.import.model"
                }),
            Text(Resources.SkinPngModelHelp)
                .FontSize(11f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Visible(!dialog.IsLegacy),
        ])
            .Spacing(6f)
            .Visible(!dialog.IsCape),
        Column([
            Text(Resources.SkinProjectName)
                .FontSize(12f),
            TextBox(dialog.Name, value => dialog.Name = value)
                .Style(LauncherStyles.Input)
                .Enabled(!dialog.Working)
                .Automation(new()
                {
                    Name = Resources.SkinProjectName,
                    AutomationId = "skin.import.name"
                }),
        ])
            .Spacing(6f)
            .Visible(!dialog.HasProject),
        Text(Resources.SkinOutfitHelp)
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(dialog.IsCompanion),
        Text(Resources.SkinPngReplaceHelp)
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(dialog.HasProject),
        Text(dialog.Error)
            .FontSize(11f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(dialog.Error.Length > 0)
            .Automation(new()
            {
                Name = dialog.Error,
                AutomationId = "skin.import.error"
            }),
    ])
        .Spacing(16f);
}

[View]
internal readonly partial record struct SkinImportViewSegments(SkinPngImportDialog Dialog, LauncherSegmentedMaterial Material)
{
    /// <summary>Applies the shared launcher material to the import preview's native view selector.</summary>
    public Ui Body => Column([
        LauncherControls.Segmented(new SkinImportViewFilter(Dialog), Material)
    ]);
}

internal sealed partial class SkinImportViewFilter(SkinPngImportDialog dialog) : Control
{
    private static SegmentedItem[] Items => [new(Resources.SkinFlat)
        {
            Automation = new()
            {
                AutomationId = "skin.import.view.flat"
            }
        }, new("3D")
        {
            Automation = new()
            {
                AutomationId = "skin.import.view.model"
            }
        }, ];

    private Ui Body => Segmented(Items)
        .SelectedIndex(dialog.Flat ? 0 : 1)
        .OnSelectionChanged(index => dialog.Flat = index == 0)
        .Style(LauncherStyles.Segmented)
        .MinHeight(32f).MinWidth(148f)
        .Automation(new() { AutomationId = "skin.import.view.switch" });
}