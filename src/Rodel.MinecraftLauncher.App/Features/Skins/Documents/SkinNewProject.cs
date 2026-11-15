// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Previews a template without creating a project until the player confirms.</summary>
internal sealed partial class SkinNewProject : Control
{
    private readonly SkinEditorController _controller;
    private readonly Action _close;
    private readonly ScrollView _scroll;
    private readonly Rendering.SkinDialogPreview _modelPreview;
    private bool _modelChosen;
    private (int Template, int Model, int Size)? _previewKey;
    private SkinRenderSnapshot? _snapshot;

    internal SkinNewProject(SkinEditorController controller, Action close, int template = 0)
    {
        _controller = controller;
        _close = close;
        Name = string.Empty;
        Error = string.Empty;
        SelectTemplate(template);
        _scroll = new(new SkinNewProjectFields(this));
        _modelPreview = new(
            controller.Window,
            () => Snapshot,
            () => Flat,
            () => false,
            error => Error = error,
            "skin.create.preview.model");
    }

    internal partial string Name { get; set; }
    internal partial int Model { get; set; }
    internal partial int Size { get; set; }
    internal partial int Template { get; set; }
    internal partial bool Flat { get; set; }
    internal partial string Error { get; set; }
    internal bool IsCape => SkinTemplates.SuggestedModel(SkinEditorController.Templates[Template]) == SkinModel.Cape;
    internal SkinRenderSnapshot Snapshot
    {
        get
        {
            EnsurePreview();
            return _snapshot!;
        }
    }

    internal ImageSource Preview
    {
        get
        {
            EnsurePreview();
            return field;
        }
        private set;
    } = null!;

    private void EnsurePreview()
    {
        // Always read every choice, including cache hits, so reactive consumers keep all dependencies.
        var key = (Template, Model, Size);
        if (_previewKey == key)
            return;
        var document = SkinTemplates.Create(SkinEditorController.Templates[key.Template], IsCape
                ? SkinModel.Cape
                : key.Model == 1 ? SkinModel.Slim : SkinModel.Classic, key.Size == 1 ? 128 : 64);
        _snapshot = new(document);
        Preview = SkinLayerThumbnail.Create(_snapshot.Pixels.ToArray(), document.Size, document.Size);
        _previewKey = key;
    }

    internal void SelectTemplate(int index)
    {
        Template = index;
        if (!_modelChosen && !IsCape)
            Model = SkinTemplates.SuggestedModel(SkinEditorController.Templates[index]) == SkinModel.Slim ? 1 : 0;
    }

    internal void SelectModel(int index)
    {
        Model = index;
        _modelChosen = true;
    }

    private float PreviewEdge => Math.Min(360f, _controller.DialogHeight - 206f);

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Star()],
        [
            Grid(
                [GridLength.Auto, GridLength.Auto, GridLength.Star(), GridLength.Auto],
                [GridLength.Auto],
                [
                    Text(Resources.SkinNew)
                        .FontSize(18f)
                        .VAlign(VerticalAlignment.Center)
                        .Cell(0, 0),
                    new SkinNewViewSegments(this, LauncherMaterials.SegmentedMaterial).Cell(1, 0),
                    Button(
                        Row([
                            LauncherIcons.Line("close", 16f)
                        ]),
                        _close)
                        .Style(SkinEditorChrome.QuietIconCommand)
                        .Width(32f).Height(32f)
                        .ToolTip(Resources.SkinCloseDialog)
                        .Automation(new()
                        {
                            Name = Resources.SkinCloseDialog,
                            AutomationId = "skin.create.close"
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
                                .Stretch(ImageStretch.Uniform)
                                .Interpolation(ImageInterpolationMode.NearestNeighbor)
                                .Width(PreviewEdge).Height(PreviewEdge)
                                .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                                .Visible(Flat)
                                .Automation(new()
                                {
                                    Name = Resources.SkinFlat,
                                    AutomationId = "skin.create.preview"
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
                                _scroll.Automation(new() { AutomationId = "skin.create.scroll" })
                                    .Cell(0, 0),
                                Border(Grid(
                                        [GridLength.Star()],
                                        [GridLength.Auto],
                                        [
                                            LauncherControls.Command(Resources.SkinCreate, () => ConfirmCreate())
                                                .Style(LauncherButtons.DialogPrimary)
                                                .MinHeight(40f)
                                                .Enabled(Name.Trim()
                                                    .Length > 0)
                                                .Automation(new() { AutomationId = "skin.create.confirm" })
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
        .Width(740f).Height(Math.Min(484f, _controller.DialogHeight - 120f));

    private void ConfirmCreate()
    {
        var name = Name.Trim();
        var template = SkinEditorController.Templates[Template];
        var model = IsCape
            ? SkinModel.Cape
            : Model == 1 ? SkinModel.Slim : SkinModel.Classic;
        var size = Size == 1 ? 128 : 64;
        _close();
        _ = _controller.CreateAsync(name, template, model, size);
    }

    protected override IDisposable? OnMounted() => PopupKeyboardClose.Bind(_controller.Window, _close, () => true);
}

internal sealed partial class SkinNewProjectFields(SkinNewProject dialog) : Control
{
    private Ui Body => Column([
        Column([
            Text(Resources.SkinProjectName)
                .FontSize(12f),
            TextBox(dialog.Name, value => dialog.Name = value)
                .Style(LauncherStyles.Input)
                .Automation(new()
                {
                    Name = Resources.SkinProjectName,
                    AutomationId = "skin.create.name"
                }),
        ])
            .Spacing(6f),
        Column([
            Text(Resources.SkinTemplate)
                .FontSize(12f),
            LauncherControls.Choice([Resources.SkinTemplateClassic, Resources.SkinTemplateAlex, Resources.SkinTemplateCasual, Resources.SkinTemplateFigure, Resources.SkinTemplateBlank, Resources.SkinTemplateCape]).MinHeight(40f)
                .SelectedIndex(dialog.Template)
                .OnSelectionChanged(dialog.SelectTemplate)
                .Automation(new()
                {
                    Name = Resources.SkinTemplate,
                    AutomationId = "skin.create.template"
                }),
        ])
            .Spacing(6f),
        Column([
            Text(Resources.SkinArmModel)
                .FontSize(12f),
            LauncherControls.Choice([Resources.SkinClassicModel, Resources.SkinSlimModel]).MinHeight(40f)
                .SelectedIndex(dialog.Model)
                .OnSelectionChanged(dialog.SelectModel)
                .Automation(new()
                {
                    Name = Resources.SkinArmModel,
                    AutomationId = "skin.create.model"
                }),
        ])
            .Spacing(6f)
            .Visible(!dialog.IsCape),
        Column([
            Text(Resources.SkinTextureSize)
                .FontSize(12f),
            LauncherControls.Choice(dialog.IsCape ? ["64 × 32", "128 × 64"] : ["64 × 64", "128 × 128"]).MinHeight(40f)
                .SelectedIndex(dialog.Size)
                .OnSelectionChanged(index => dialog.Size = index)
                .Automation(new()
                {
                    Name = Resources.SkinTextureSize,
                    AutomationId = "skin.create.size"
                }),
            Text(Resources.SkinHdUsage)
                .FontSize(11f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Visible(dialog.Size == 1 && !dialog.IsCape),
        ])
            .Spacing(6f),
        Text(dialog.Error)
            .FontSize(11f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(dialog.Error.Length > 0)
            .Automation(new()
            {
                Name = dialog.Error,
                AutomationId = "skin.create.error"
            }),
    ])
        .Spacing(16f);
}

[View]
internal readonly partial record struct SkinNewViewSegments(SkinNewProject Dialog, LauncherSegmentedMaterial Material)
{
    /// <summary>Uses the shared segmented material for the new project's mutually exclusive preview views.</summary>
    public Ui Body => Column([
        LauncherControls.Segmented(new SkinNewViewFilter(Dialog), Material)
    ]);
}

internal sealed partial class SkinNewViewFilter(SkinNewProject dialog) : Control
{
    private static SegmentedItem[] Items => [new(Resources.SkinFlat)
        {
            Automation = new()
            {
                AutomationId = "skin.create.view.flat"
            }
        }, new("3D")
        {
            Automation = new()
            {
                AutomationId = "skin.create.view.model"
            }
        }, ];

    private Ui Body => Segmented(Items)
        .SelectedIndex(dialog.Flat ? 0 : 1)
        .OnSelectionChanged(index => dialog.Flat = index == 0)
        .Style(LauncherStyles.Segmented)
        .MinHeight(32f).MinWidth(148f)
        .Automation(new() { AutomationId = "skin.create.view.switch" });
}