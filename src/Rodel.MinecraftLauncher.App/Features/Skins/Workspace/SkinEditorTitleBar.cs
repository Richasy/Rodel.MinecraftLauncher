// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reactive;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Uses Sprout's native interactive titlebar band; caption buttons and drag/Snap remain framework-owned.</summary>
internal sealed partial class SkinEditorTitleBar(SkinEditorState editor, Window window) : Control
{
    private readonly MenuFlyout _menu = new();
    private readonly MenuFlyout _export = new();
    private readonly Flyout _tools = Popup(close => new SkinEditorToolOptions(editor, close));

    private static Flyout Popup(Func<Action, Control> content) => new(close => content(close))
    {
        Placement = PopupPlacement.BottomEdgeAlignedLeft,
        ShowMode = FlyoutShowMode.Standard,
        ShouldConstrainToRootBounds = true,
    };
    private ImageSource AppIcon => window.Icon?.AsImage ?? ImageSource.FromFile(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "launcher.png"));

    private Ui Body => Grid(
        [GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Star(), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto],
        [GridLength.Star()],
        [
            Image(AppIcon)
                .Width(24f).Height(24f)
                .VAlign(VerticalAlignment.Center)
                .Margin(new Thickness(4f, 0f, 8f, 0f))
                .Automation(new()
                {
                    Name = Resources.SkinEditorTitle,
                    AutomationId = "skin.logo"
                })
                .Cell(0, 0),
            Text(Resources.SkinEditorTitle)
                .FontSize(14f).Foreground(LauncherTheme.Ink)
                .VAlign(VerticalAlignment.Center)
                .Automation(new() { Name = Resources.SkinEditorTitle, AutomationId = "skin.start.title" })
                .Visible(!editor.HasProject)
                .Cell(1, 0),
            DropDownButton(editor.ProjectName, _menu.Style(LauncherMenus.CommandMenu))
                .Style(SkinEditorChrome.TitleTrigger)
                .MaxWidth(editor.Compact ? 160f : 240f)
                .Automation(new()
                {
                    Name = Resources.SkinProjectMenu,
                    AutomationId = "skin.menu"
                })
                .Visible(editor.HasProject)
                .Cell(1, 0),
            Rectangle(LauncherTheme.Edge)
                .Width(1f).Height(18f)
                .Margin(new Thickness(4f, 0f, 4f, 0f))
                .Visible(editor.HasProject)
                .Cell(2, 0),
            DropDownButton(editor.Tool == 4 ? editor.ToolName : string.Format(Resources.SkinToolSummary, editor.ToolName, editor.BrushSize), _tools.LauncherStyle(popupAppearance=>popupAppearance.Menus.ErrorPopup.Background(popupAppearance.Materials.Acrylic)
                        .Border(popupAppearance.Edge)
                        .Padding(16f)))
                .Style(SkinEditorChrome.TitleTrigger)
                .Automation(new()
                {
                    Name = Resources.SkinToolOptions,
                    AutomationId = "skin.tool.options"
                })
                .Visible(editor.HasProject)
                .Cell(3, 0),
            Button(
                Row([
                    LauncherIcons.Line("skin-undo", 16f)
                ]),
                () => _ = editor.Controller.RestoreAsync(false))
                .Style(SkinEditorChrome.QuietIconCommand)
                .Enabled(editor.CanUndo && !editor.Ai.IsEditing && !editor.Selecting && !editor.Busy && !editor.HasLayerDraft && !editor.FileActive && !editor.LayerDragging)
                .Width(32f).Height(32f)
                .ToolTip(Resources.SkinUndo)
                .Automation(new()
                {
                    Name = Resources.SkinUndo,
                    AcceleratorKey = "Ctrl+Z",
                    AutomationId = "skin.undo"
                })
                .Visible(editor.HasProject)
                .Cell(5, 0),
            Button(
                Row([
                    LauncherIcons.Line("skin-redo", 16f)
                ]),
                () => _ = editor.Controller.RestoreAsync(true))
                .Style(SkinEditorChrome.QuietIconCommand)
                .Enabled(editor.CanRedo && !editor.Ai.IsEditing && !editor.Selecting && !editor.Busy && !editor.HasLayerDraft && !editor.FileActive && !editor.LayerDragging)
                .Width(32f).Height(32f)
                .ToolTip(Resources.SkinRedo)
                .Automation(new()
                {
                    Name = Resources.SkinRedo,
                    AcceleratorKey = "Ctrl+Y",
                    AutomationId = "skin.redo"
                })
                .Visible(editor.HasProject)
                .Cell(6, 0),
            LauncherControls.Command(Resources.SkinPreview, editor.Controller.ShowPreview)
                .Style(LauncherButtons.DialogSecondary.Padding(12f, 4f)
                    .LabelStyle(LauncherStyles.Text.FontSize(12f)))
                .Enabled(!editor.Busy && !editor.Selecting && !editor.FileActive && !editor.LayerDragging)
                .MinHeight(30f)
                .VAlign(VerticalAlignment.Center)
                .Automation(new() { AutomationId = "skin.preview" })
                .Visible(editor.HasProject)
                .Cell(7, 0),
            LauncherControls.Command(Resources.SkinExport, () => _ = editor.Controller.Files.ExportAsync())
                .Style(LauncherButtons.DialogPrimary.Padding(12f, 4f)
                    .LabelStyle(LauncherStyles.Text.FontSize(12f)))
                .Enabled(editor.Controller.Files.CanExport)
                .MinHeight(30f)
                .VAlign(VerticalAlignment.Center)
                .Automation(new()
                {
                    AcceleratorKey = "Ctrl+Shift+E",
                    AutomationId = "skin.export"
                })
                .Visible(editor.HasProject && editor.TextureSize == 64)
                .Cell(8, 0),
            SplitButton(new LauncherSplitButtonLabel(Resources.SkinExport, LauncherStyles.Text.FontSize(12f).Foreground(LauncherTheme.PrimaryForeground)), () => _ = editor.Controller.Files.ExportAsync(), _export.Style(LauncherMenus.CommandMenu))
                .Style(LauncherMenus.AccentSplitCommand)
                .CornerRadius(CornerRadius.Zero)
                .Padding(new Thickness(12f, 4f, 12f, 4f))
                .SurfacePainter(LauncherSplitButtonSurface.PaintAccent)
                .Enabled(editor.Controller.Files.CanExport)
                .MinHeight(30f)
                .VAlign(VerticalAlignment.Center)
                .Automation(new()
                {
                    Name = Resources.SkinExport,
                    AcceleratorKey = "Ctrl+Shift+E",
                    AutomationId = "skin.export.hd"
                })
                .Visible(editor.HasProject && editor.TextureSize == 128)
                .Cell(8, 0),
        ])
        .ColumnSpacing(editor.Compact ? 4f : 8f)
        .Padding(new Thickness(16f, 6f, 8f, 6f))
        .MinHeight(48f);

    protected override IDisposable? OnMounted() => new Effect(() =>
        {
            _menu.Hide();
            _menu.Items.Clear();
            _menu.Items.Add(new MenuFlyoutItem(Resources.SkinNew, () => editor.Controller.ShowNew())
            {
                Icon = new SymbolIcon(Symbol.Add),
                IsEnabled = editor.Controller.Files.Available,
                AcceleratorText = "Ctrl+N",
                Automation = new()
                {
                    AcceleratorKey = "Ctrl+N",
                    AutomationId = "skin.new"
                }
            });
            _menu.Items.Add(new MenuFlyoutItem(Resources.SkinImportCape, editor.Controller.Files.ShowImportCape)
            {
                IsEnabled = editor.Controller.Files.Available,
                Automation = new() { AutomationId = "skin.import-cape" }
            });
            _menu.Items.Add(new MenuFlyoutItem(Resources.SkinOpen, editor.Controller.ShowProjects)
            {
                Icon = new SymbolIcon(Symbol.Folder),
                IsEnabled = editor.Controller.Files.Available,
                AcceleratorText = "Ctrl+O",
                Automation = new()
                {
                    AcceleratorKey = "Ctrl+O",
                    AutomationId = "skin.open"
                }
            });
            _menu.Items.Add(new MenuFlyoutItem(Resources.SkinSaveAs, editor.Controller.Files.ShowSaveAs)
            {
                Icon = new SymbolIcon(Symbol.Save),
                IsEnabled = editor.Controller.Files.CanExport,
                AcceleratorText = "Ctrl+Shift+S",
                Automation = new()
                {
                    AcceleratorKey = "Ctrl+Shift+S",
                    AutomationId = "skin.save-as"
                }
            });
            if (editor.HasLayerDraft || (editor.Controller.Session?.HasDraft == true) || editor.Controller.HasDeferredStroke)
            {
                _menu.Items.Add(new MenuFlyoutItem(Resources.SkinDiscardStroke, editor.Controller.Cancel)
                {
                    Icon = new SymbolIcon(Symbol.Delete) { Foreground = LauncherTheme.Error },
                    Automation = new() { AutomationId = "skin.stroke.discard" }
                });
            }

            _menu.Items.Add(new MenuFlyoutItem(Resources.SkinImport, editor.Controller.Files.ShowImport)
            {
                Icon = new SymbolIcon(Symbol.OpenFile),
                AcceleratorText = "Ctrl+Shift+I",
                IsEnabled = editor.Controller.Files.Available,
                Automation = new()
                {
                    AcceleratorKey = "Ctrl+Shift+I",
                    AutomationId = "skin.import"
                }
            });
            _export.Hide();
            _export.Items.Clear();
            _export.Items.Add(new MenuFlyoutItem(editor.IsCape ? "64 × 32 PNG" : Resources.SkinExport64, () => DispatcherQueue.GetForCurrentThread().TryEnqueue(() => _ = editor.Controller.Files.ExportAsync(64)))
            {
                Icon = new SymbolIcon(Symbol.Save),
                IsEnabled = editor.Controller.Files.CanExport && editor.TextureSize == 128,
                Automation = new() { AutomationId = "skin.export.64" },
            });
        });

    protected override void OnDispose()
    {
        _menu.Hide();
        _tools.Hide();
        _export.Hide();
        base.OnDispose();
    }
}

[Sprout.Reconcile.View]
internal readonly partial record struct SkinEditorAppIconContent(ImageSource Source)
{
    /// <summary>Composes the native controls for this editor region from the supplied interface choices.</summary>
    public Ui Body => Image(Source)
        .Width(24f).Height(24f);
}

[Sprout.Reconcile.View]
internal readonly partial record struct SkinTransferCommand(
    LauncherAppearance Theme,
    string Label,
    string Icon,
    Action Action,
    bool Enabled,
    string Id,
    string Key,
    bool Primary = false)
{
    /// <summary>Displays one localized selection command with a shared icon, command surface and keyboard identity.</summary>
    public Ui Body => Row([
        Button(
            Row([
                LauncherIcons.Line(Icon, 16f),
                Text(Label)
                    .FontSize(12f)
            ])
                .Spacing(8f),
            Action)
            .Style((Primary ? Theme.Buttons.DialogPrimary : Theme.Buttons.DialogSecondary).Padding(12f, 6f))
            .Enabled(Enabled)
            .MinHeight(34f)
            .Weight(1f)
            .Automation(new()
            {
                Name = Label,
                AutomationId = Id,
                AcceleratorKey = Key
            }),
    ]);
}