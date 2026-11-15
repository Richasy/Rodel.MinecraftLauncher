// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

[View]
internal readonly partial record struct SkinLayerRowHost(SkinLayerRowData Data, SkinEditorLayerList Owner)
{
    /// <summary>Retains a control per stable layer identity while item data changes.</summary>
    public Ui Body => new SkinLayerRow(Data, Owner);
}

/// <summary>Separates selection, visibility, inline naming and drag/menu hit regions in one layer row.</summary>
internal sealed partial class SkinLayerRow : Control
{
    internal SkinLayerRow(SkinLayerRowData data, SkinEditorLayerList owner)
    {
        DataValue = data;
        Owner = owner;
        _input = new(owner.Editor);
        _more = new SkinLayerMore(() => Data, ShowMenu);
    }

    private partial SkinLayerRowData DataValue { get; set; }
    internal SkinLayerRowData Data => DataValue;
    private partial SkinEditorLayerList Owner { get; set; }
    private readonly SkinLayerNameInput _input;
    private PopupFocusRecovery? _focus;
    private MenuFlyout? _menu;
    private SkinEditorState Editor => Owner.Editor;
    private bool Background => Data.Layer.Id == SkinDocument.BackgroundLayerId;
    private bool Editing => Editor.LayerEditing && (Editor.LayerRenameId == Data.Layer.Id);
    private string Name => Background && (Data.Layer.Name == "Background") ? Resources.SkinBackgroundLayer : Data.Layer.Name;
    private string VisibilityLabel => Data.Layer.Visible ? Resources.SkinLayerHide : Resources.SkinLayerShow;
    private readonly SkinLayerMore _more;

    private Ui Body => PointerArea(
        Border(Grid(
                [GridLength.Pixels(32f), GridLength.Star(), GridLength.Pixels(22f), GridLength.Pixels(22f)],
                [GridLength.Star()],
                [
                    Image(Data.Thumbnail)
                        .Interpolation(ImageInterpolationMode.NearestNeighbor)
                        .Width(32f).Height(32f)
                        .VAlign(VerticalAlignment.Center)
                        .Cell(0, 0),
                    Text(Name)
                        .FontSize(11f)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .VAlign(VerticalAlignment.Center)
                        .Visible(!Editing)
                        .Cell(1, 0),
                    _input.Visible(Editing)
                        .VAlign(VerticalAlignment.Center)
                        .Cell(1, 0),
                    Button(
                        Row([
                            LauncherIcons.Line(Data.Layer.Visible ? "layer-eye" : "layer-eye-off", 15f)
                        ]),
                        ToggleVisibility)
                        .Style(SkinEditorChrome.QuietIconCommand)
                        .Enabled(Data.Enabled)
                        .Visible(!Background)
                        .Width(22f).Height(28f)
                        .VAlign(VerticalAlignment.Center)
                        .ToolTip(VisibilityLabel)
                        .Automation(new()
                        {
                            Name = VisibilityLabel,
                            AutomationId = "skin.layer.visibility." + Data.Layer.Id
                        })
                        .Cell(2, 0),
                    _more.Cell(3, 0),
                ])
                .ColumnSpacing(8f))
            .Padding(12f)
            .MinHeight(56f),
        HandlePointer);

    private PointerEventResult HandlePointer(in PointerEvent packet)
    {
        if ((packet.Kind == PointerEventKind.Down) && (packet.Button == PointerButton.Primary) && (packet.ClickCount == 2) && !Editing)
        {
            BeginRename();
            return new(true, PointerCaptureRequest.None);
        }

        return PointerEventResult.Ignored;
    }

    private void ToggleVisibility() => _ = Editor.Controller.LayerActions.ApplyAsync(new()
    {
        Operation = SkinLayerOperation.Set,
        Id = Data.Layer.Id,
        Visible = !Data.Layer.Visible
    });

    private void BeginRename()
    {
        if (Editor.Controller.LayerActions.BeginRename(Data.Layer.Id, Name))
            _focus?.Request(_input);
    }

    private void QueueRename()
    {
        var id = Data.Layer.Id;
        var session = Editor.Controller.Session;
        // Menu dismissal restores its trigger focus; start the editor only after that lifecycle completes.
        DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
            {
                if (_focus is not null && (Data.Layer.Id == id) && ReferenceEquals(Editor.Controller.Session, session))
                    BeginRename();
            });
    }

    internal void ShowMenu()
    {
        var index = Array.FindIndex(Editor.Layers, layer => layer.Id == Data.Layer.Id);
        _menu?.Hide();
        _menu = new MenuFlyout([new MenuFlyoutItem(Resources.SkinLayerRename, QueueRename)
            {
                Icon = new SymbolIcon(Symbol.Edit),
                AcceleratorText = "F2",
                Automation = new() { AutomationId = "skin.layer.rename." + Data.Layer.Id }
            }, new MenuFlyoutItem(Resources.SkinLayerMoveUp, () => _ = Owner.MoveAsync(Data.Layer.Id, -1))
            {
                Icon = new SymbolIcon(Symbol.Up),
                AcceleratorText = "Alt+↑",
                IsEnabled = !Background && (index < (Editor.Layers.Length - 1)),
                Automation = new() { AutomationId = "skin.layer.up." + Data.Layer.Id }
            }, new MenuFlyoutItem(Resources.SkinLayerMoveDown, () => _ = Owner.MoveAsync(Data.Layer.Id, 1))
            {
                Icon = new FontIcon("\uE74B"),
                AcceleratorText = "Alt+↓",
                IsEnabled = !Background && (index > 1),
                Automation = new() { AutomationId = "skin.layer.down." + Data.Layer.Id }
            }, new MenuFlyoutSeparator(), new MenuFlyoutItem(Resources.SkinLayerExportPng, () => Editor.Controller.Files.ShowExportLayer(Data.Layer.Id))
            {
                IsEnabled = Editor.Controller.Files.CanExport,
                Icon = new SymbolIcon(Symbol.Save),
                Automation = new() { AutomationId = "skin.layer.export." + Data.Layer.Id }
            }, new MenuFlyoutItem(Resources.SkinLayerDelete, () => _ = Editor.Controller.LayerActions.ApplyAsync(new()
                    {
                        Operation = SkinLayerOperation.Remove,
                        Id = Data.Layer.Id
                    }))
            {
                IsEnabled = !Background && !Data.Layer.Locked,
                Icon = new SymbolIcon(Symbol.Delete) { Foreground = LauncherTheme.Error },
                Automation = new() { AutomationId = "skin.layer.delete." + Data.Layer.Id }
            }, ]).Style(LauncherMenus.CommandMenu);
        if (_more.Interaction is { } anchor)
            _menu.ShowAt(Editor.Controller.Window, anchor);
    }

    private bool ContainsFocus()
    {
        for (var node = Editor.Controller.Window.FocusedElement; node is not null; node = node.Parent)
        {
            if (ReferenceEquals(node, Interaction))
                return true;
        }

        if (!Data.Selected)
            return false;
        var focused = Editor.Controller.Window.FocusedElement;
        for (var node = Interaction; node is not null && !ReferenceEquals(node, Owner.Interaction); node = node.Parent)
        {
            if (ReferenceEquals(node, focused))
                return true;
        }

        return false;
    }

    private void KeyDown(KeyEvent key)
    {
        if (key.IsImeOwned || key.Repeat || Editing || !ContainsFocus())
            return;
        if (key.Key == Key.F2)
            BeginRename();
        if (((key.Modifiers & KeyModifiers.Alt) != 0) && key.Key is Key.ArrowUp or Key.ArrowDown)
        {
            _ = Owner.MoveAsync(Data.Layer.Id, key.Key == Key.ArrowUp ? -1 : 1);
        }
    }

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is SkinLayerRow other)
        {
            DataValue = other.Data;
            Owner = other.Owner;
        }
    }

    protected override IDisposable? OnMounted()
    {
        _focus = new(Editor.Controller.Window);
        Editor.Controller.Window.KeyDown += KeyDown;
        return null;
    }

    protected override void OnDispose()
    {
        Editor.Controller.Window.KeyDown -= KeyDown;
        _focus?.Dispose();
        _focus = null;
        _menu?.Hide();
        base.OnDispose();
    }
}

internal sealed partial class SkinLayerNameInput(SkinEditorState editor) : Control
{
    private Ui Body => TextBox(editor.LayerRenameName, value => editor.LayerRenameName = value)
        .Style(LauncherStyles.Input.ContentFontSize(11f)
            .ContentPadding(new Thickness(4f, 0f, 4f, 0f)))
        .SubmitOnEnter()
        .OnSubmitted(value => _ = editor.Controller.LayerActions.CommitRenameAsync())
        .OnLostFocus(() => _ = editor.Controller.LayerActions.CommitRenameAsync())
        .Enabled(!editor.Busy)
        .MinHeight(30f)
        .Automation(new()
        {
            Name = Resources.SkinLayerRename,
            AutomationId = "skin.layer.rename.input"
        });
}

/// <summary>Provides a stable popup anchor at the actual overflow button.</summary>
internal sealed partial class SkinLayerMore(Func<SkinLayerRowData> current, Action open) : Control
{
    private SkinLayerRowData Data => current();

    private Ui Body => Column([
        Button(
            Row([
                LauncherIcons.Line("more", 15f)
            ]),
            open)
            .Style(SkinEditorChrome.QuietIconCommand)
            .Enabled(Data.Enabled)
            .Width(22f).Height(28f)
            .ToolTip(Resources.SkinLayerOptions)
            .Automation(new()
            {
                Name = Resources.SkinLayerOptions,
                AutomationId = "skin.layer.more." + Data.Layer.Id
            })
    ])
        .VAlign(VerticalAlignment.Center);
}