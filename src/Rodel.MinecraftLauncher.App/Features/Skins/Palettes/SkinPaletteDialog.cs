// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

internal enum SkinPaletteMode
{
    Manage,
    New,
    Copy,
    CopyAndAdd,
    Delete,
    Reference,
}

/// <summary>Edits saved palettes in place and stages new palettes until their explicit creation.</summary>
internal sealed partial class SkinPaletteDialog : Control
{
    private const float Inset = 12f;
    private readonly SkinEditorState _editor;
    private readonly Action _close;
    private readonly ScrollView _scroll;
    private readonly SkinPaletteMore _more;
    private readonly SkinPaletteNameInput _rename;
    private readonly SkinPaletteNameInput _name;
    private readonly SkinPaletteCancelButton _cancel;
    private readonly bool _standalone;
    private PopupFocusRecovery? _focus;
    private MenuFlyout? _menu;
    private readonly Dictionary<int, Flyout> _pickers = [];
    internal SkinPaletteController Palettes => _editor.Palettes;
    internal SkinPaletteReferenceView Reference { get; }

    internal SkinPaletteDialog(SkinEditorState editor, Action close, SkinPaletteMode mode, SkinPaletteReference? reference)
    {
        _editor = editor;
        _close = close;
        Name = string.Empty;
        Hex = string.Empty;
        Error = string.Empty;
        DraftColors = [];
        DraftForeground = editor.Foreground;
        _standalone = mode is SkinPaletteMode.New or SkinPaletteMode.Reference;
        Reference = new(editor.Controller.Window, reference, AddSample);
        Reference.WithSize(reference is null ? 400f : 560f, ReferenceHeight);
        _more = new(this);
        _rename = new(this, inline: true);
        _name = new(this, inline: false);
        _cancel = new(this);
        _scroll = new(new SkinPaletteFields(this, _name));
        Selected = Math.Max(0, Array.FindIndex(Palettes.Current.Colors, color => color.Equals(editor.Foreground, StringComparison.OrdinalIgnoreCase)));
        Hex = ColorSelected ? Palettes.Current.Colors[Selected] : string.Empty;
        if (mode != SkinPaletteMode.Manage)
        {
            Begin(mode);
            if (reference is not null)
            {
                DraftColors = [.. reference.Colors];
                Name = Palettes.SuggestName(copy: false, System.IO.Path.GetFileNameWithoutExtension(reference.Name));
                Selected = DraftColors.Length > 0 ? 0 : -1;
                Hex = ColorSelected ? Colors[Selected] : string.Empty;
            }
        }
    }

    internal partial SkinPaletteMode Mode { get; set; }
    internal partial int Selected { get; set; }
    internal partial string Name { get; set; }
    internal partial string Hex { get; set; }
    internal partial string Error { get; set; }
    internal partial bool Renaming { get; set; }
    private partial string[] DraftColors { get; set; }
    private partial string DraftForeground { get; set; }
    private partial float RetainedHeight { get; set; }
    internal bool Creating => Mode is SkinPaletteMode.New or SkinPaletteMode.Copy or SkinPaletteMode.CopyAndAdd or SkinPaletteMode.Reference;
    internal bool ReadOnly => !Creating && Palettes.IsBuiltIn;
    internal string[] Colors => Creating ? DraftColors : Palettes.Current.Colors;
    internal bool ColorSelected => (uint)Selected < (uint)Colors.Length;
    internal bool ValidHex => SkinPaletteStore.TryNormalizeColor(Hex, out _);
    internal bool CanAdd => Palettes.CanWrite && (Creating || Palettes.HasPalette) && SkinPaletteStore.TryNormalizeColor(Foreground, out _);
    internal string Foreground => Creating ? DraftForeground : _editor.Foreground;
    internal float ContentWidth => Mode == SkinPaletteMode.Reference ? 560f : 400f;
    internal float ReferenceHeight => Math.Clamp(_editor.Height - 410f, 160f, 280f);
    internal Color SelectedColor
    {
        get
        {
            if (!ColorSelected)
                return Sprout.Graphics.Colors.Transparent;
            return SkinEditorState.TryParseColor(Colors[Selected], out var color) ? color : throw new InvalidDataException("The selected palette color is invalid.");
        }
    }

    internal string Title => Mode switch
    {
        SkinPaletteMode.New => Resources.SkinPaletteNew,
        SkinPaletteMode.Copy or SkinPaletteMode.CopyAndAdd => Resources.SkinPaletteCopy,
        SkinPaletteMode.Delete => Resources.SkinPaletteDelete,
        SkinPaletteMode.Reference => Resources.SkinPaletteFromImage,
        _ => SkinPaletteController.DisplayName(Palettes.Current)
    };
    private float HeaderHeight => Mode == SkinPaletteMode.Manage ? 40f : 32f;
    private float ScrollMaximum => Math.Max(100f, _editor.Height - 200f);
    private float FixedSectionHeight => HeaderHeight + (4f * Inset) + 1f + (Mode == SkinPaletteMode.Manage ? 0f : 38f + (2f * Inset) + 1f);
    private float ScrollMinimum => Math.Min(ScrollMaximum, Math.Max(0f, RetainedHeight - FixedSectionHeight));

    private Ui Body => Column([
        Border(Row([
                Column([
                    Text(Title)
                        .FontSize(18f).FontWeight(600)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .Visible(!Renaming)
                        .Automation(new()
                        {
                            Name = Title,
                            AutomationId = "skin.palette.title"
                        }),
                    _rename.Visible(Renaming),
                    Text(string.Format(Resources.SkinPaletteColorCount, Colors.Length))
                        .FontSize(11f).Foreground(LauncherTheme.Muted)
                        .Visible(Mode == SkinPaletteMode.Manage),
                ])
                    .Spacing(4f)
                    .Weight(1f)
                    .VAlign(Mode == SkinPaletteMode.Manage ? VerticalAlignment.Top : VerticalAlignment.Center),
                Button(
                    Row([
                        LauncherIcons.Line("edit", 16f)
                    ]),
                    () => BeginRename())
                    .Style(SkinEditorChrome.QuietIconCommand)
                    .Width(28f).Height(28f)
                    .VAlign(VerticalAlignment.Top)
                    .Visible(Mode == SkinPaletteMode.Manage && Palettes.HasPalette && !Palettes.IsBuiltIn && !Renaming)
                    .Enabled(Palettes.CanWrite)
                    .ToolTip(Resources.SkinPaletteRename)
                    .Automation(new()
                    {
                        Name = Resources.SkinPaletteRename,
                        AutomationId = "skin.palette.rename"
                    }),
                _more.Visible(Mode == SkinPaletteMode.Manage && Palettes.HasPalette)
                    .VAlign(VerticalAlignment.Top),
                LauncherControls.OutlineCommand(
                    Row([
                        LauncherIcons.Line("close", 16f)
                    ]),
                    _close)
                    .Style(LauncherButtons.PopupCloseButton)
                    .Width(28f).Height(28f)
                    .VAlign(VerticalAlignment.Top)
                    .ToolTip(Resources.SkinCloseDialog)
                    .Visible(Mode == SkinPaletteMode.Manage)
                    .Automation(new()
                    {
                        Name = Resources.SkinCloseDialog,
                        AutomationId = "skin.palette.close"
                    }),
            ])
                .Spacing(8f)
                .MinHeight(HeaderHeight))
            .Padding(Inset)
            .BorderBrush(LauncherTheme.Edge)
            .BorderThickness(0f, 0f, 0f, 1f),
        Border(_scroll.MinHeight(ScrollMinimum).MaxHeight(ScrollMaximum)
                .Automation(new() { AutomationId = "skin.palette.scroll" }))
            .Padding(Inset),
        Border(Grid(
                [GridLength.Star(), GridLength.Star()],
                [GridLength.Auto],
                [
                    LauncherControls.Command(Mode == SkinPaletteMode.Delete
                            ? Resources.SkinPaletteDelete
                            : Mode is SkinPaletteMode.New or SkinPaletteMode.Reference ? Resources.SkinPaletteCreate : Resources.SkinPaletteCreateCopy, () => Confirm())
                        .Style(Mode == SkinPaletteMode.Delete ? LauncherButtons.DialogSecondary : LauncherButtons.DialogPrimary)
                        .MinHeight(38f)
                        .Enabled(Palettes.CanWrite)
                        .Automation(new() { AutomationId = "skin.palette.confirm" })
                        .Cell(0, 0),
                    _cancel.Cell(1, 0),
                ])
                .ColumnSpacing(12f))
            .Padding(Inset)
            .BorderBrush(LauncherTheme.Edge)
            .BorderThickness(0f, 1f, 0f, 0f)
            .Visible(Mode != SkinPaletteMode.Manage),
    ])
        .Width(ContentWidth + (2f * Inset));

    internal void Choose(int index)
    {
        Selected = index;
        Hex = ColorSelected ? Colors[index] : string.Empty;
        Error = string.Empty;
        if (ColorSelected)
        {
            if (Creating)
                DraftForeground = Hex;
            else
                Palettes.Pick(index);
        }
    }

    internal void Begin(SkinPaletteMode mode)
    {
        if (_focus is not null)
            RetainedHeight = Math.Max(RetainedHeight, Render.Bounds.Height);
        Renaming = false;
        Mode = mode;
        Error = string.Empty;
        var copy = mode is SkinPaletteMode.Copy or SkinPaletteMode.CopyAndAdd;
        Name = Palettes.SuggestName(copy);
        DraftColors = copy ? [.. Palettes.Current.Colors] : [];
        DraftForeground = _editor.Foreground;
        if (mode == SkinPaletteMode.CopyAndAdd)
            DraftColors = [.. DraftColors, DraftForeground];
        Selected = DraftColors.Length > 0 ? 0 : -1;
        Hex = ColorSelected ? Colors[Selected] : string.Empty;
        _focus?.Request(mode == SkinPaletteMode.Delete ? _cancel : _name);
    }

    internal void Back()
    {
        if (_standalone)
        {
            _close();
            return;
        }

        Renaming = false;
        Mode = SkinPaletteMode.Manage;
        Error = string.Empty;
        RetainedHeight = 0f;
    }

    private void BeginRename()
    {
        Name = Palettes.Current.Name;
        Renaming = true;
        Error = string.Empty;
        _focus?.Request(_rename);
    }

    internal void CommitName()
    {
        if (!Renaming)
            return;
        Error = Palettes.NameError(Name, rename: true);
        if (Error.Length == 0 && Palettes.Run(store => store.Rename(Name)))
            Renaming = false;
    }

    internal void Confirm()
    {
        if (Mode == SkinPaletteMode.Manage)
            return;
        if (Mode != SkinPaletteMode.Delete)
        {
            Error = Palettes.NameError(Name);
            if (Error.Length > 0)
                return;
        }

        var adding = Mode == SkinPaletteMode.CopyAndAdd;
        var succeeded = Mode == SkinPaletteMode.Delete ? Palettes.Run(static store => store.Delete()) : Palettes.Run(store => store.Create(Name, DraftColors));
        if (!succeeded)
            return;
        if (Mode == SkinPaletteMode.Delete && !Palettes.HasPalette)
        {
            _close();
            return;
        }

        Back();
        if (_standalone)
            return;
        if (adding)
        {
            OpenAddedColor();
            return;
        }

        Selected = Math.Max(0, Array.FindIndex(Palettes.Current.Colors, color => color.Equals(_editor.Foreground, StringComparison.OrdinalIgnoreCase)));
        Hex = ColorSelected ? Palettes.Current.Colors[Selected] : string.Empty;
    }

    internal void SetHex(string value)
    {
        Hex = value;
        if (!ColorSelected || !SkinPaletteStore.TryNormalizeColor(value, out var normalized))
            return;
        if (Creating)
        {
            var colors = DraftColors.ToArray();
            colors[Selected] = normalized;
            DraftColors = colors;
            DraftForeground = normalized;
        }
        else if (Palettes.Run(store => store.SetColor(Selected, normalized)))
        {
            _editor.Foreground = normalized;
        }
    }

    internal void SetColor(Color value) => SetHex($"#{value.R:X2}{value.G:X2}{value.B:X2}{value.A:X2}");

    internal void Add()
    {
        if (!CanAdd)
            return;
        if (ReadOnly)
        {
            Begin(SkinPaletteMode.CopyAndAdd);
            return;
        }

        if (Creating)
        {
            DraftColors = [.. DraftColors, DraftForeground];
            OpenAddedColor();
        }
        else if (Palettes.Run(store => store.AddColor(_editor.Foreground)))
        {
            OpenAddedColor();
        }
    }

    private void AddSample(string color)
    {
        if (!Creating)
            return;
        DraftColors = [.. DraftColors, color];
        Selected = DraftColors.Length - 1;
        Hex = DraftForeground = color;
    }

    private void OpenAddedColor()
    {
        var index = Colors.Length - 1;
        Choose(index);
        if (Interaction is { } anchor)
            ColorPicker(index).ShowAt(_editor.Controller.Window, anchor);
    }

    internal bool Remove()
    {
        if (!ColorSelected || ReadOnly)
            return false;
        if (Creating)
        {
            DraftColors = [.. DraftColors.Where((_, index) => index != Selected)];
            Selected = Math.Min(Selected, Colors.Length - 1);
            Hex = ColorSelected ? Colors[Selected] : string.Empty;
            return true;
        }

        if (Palettes.Run(store => store.RemoveColor(Selected)))
        {
            Selected = Math.Min(Selected, Palettes.Current.Colors.Length - 1);
            Hex = ColorSelected ? Palettes.Current.Colors[Selected] : string.Empty;
            return true;
        }

        return false;
    }

    internal Flyout ColorPicker(int index)
    {
        if (_pickers.TryGetValue(index, out var retained))
            return retained;
        var picker = new Flyout(close =>
            {
                Choose(index);
                return new SkinPaletteColorPopup(this, close);
            })
        {
            Placement = PopupPlacement.BottomEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Standard,
            ShouldConstrainToRootBounds = true,
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Background(popupAppearance.Materials.Acrylic).Border(popupAppearance.Edge).Padding(16f));
        _pickers.Add(index, picker);
        return picker;
    }

    internal void ShowMenu()
    {
        _menu?.Hide();
        _menu = new MenuFlyout(new MenuFlyoutItem(Resources.SkinPaletteCopy, () => QueueMode(SkinPaletteMode.Copy))
        {
            Icon = new SymbolIcon(Symbol.Copy),
            IsEnabled = Palettes.CanWrite,
            Automation = new() { AutomationId = "skin.palette.copy" }
        });
        if (Palettes.HasPalette)
        {
            _menu.Items.Add(new MenuFlyoutItem(Resources.SkinPaletteDelete, () => QueueMode(SkinPaletteMode.Delete))
            {
                Icon = new SymbolIcon(Symbol.Delete) { Foreground = LauncherTheme.Error },
                IsEnabled = Palettes.CanWrite,
                Automation = new() { AutomationId = "skin.palette.delete" }
            });
        }

        _menu.Style(LauncherMenus.CommandMenu);
        if (_more.Interaction is { } anchor)
            _menu.ShowAt(_editor.Controller.Window, anchor);
    }

    private void QueueMode(SkinPaletteMode mode) => DispatcherQueue.GetForCurrentThread().TryEnqueue(() => Begin(mode));

    private void Escape()
    {
        if (Reference.StopPicking())
            return;
        if (Mode != SkinPaletteMode.Manage || Renaming)
            Back();
        else
            _close();
    }

    protected override IDisposable? OnMounted()
    {
        _focus = new(_editor.Controller.Window);
        if (Creating)
            _focus.Request(_name);
        else if (Mode == SkinPaletteMode.Delete)
            _focus.Request(_cancel);
        return PopupKeyboardClose.Bind(_editor.Controller.Window, Escape, () => _menu?.IsOpen != true && !_pickers.Values.Any(picker => picker.IsOpen));
    }

    protected override void OnDispose()
    {
        _menu?.Hide();
        foreach (var picker in _pickers.Values)
            picker.Hide();
        _pickers.Clear();
        _focus?.Dispose();
        _focus = null;
        base.OnDispose();
    }
}

internal sealed partial class SkinPaletteCancelButton(SkinPaletteDialog dialog) : Control
{
    private Ui Body => Row([
        LauncherControls.Command(Resources.SkinPaletteCancel, () => dialog.Back())
            .Style(dialog.Mode == SkinPaletteMode.Delete ? LauncherButtons.DialogPrimary : LauncherButtons.DialogSecondary)
            .MinHeight(38f).Weight(1f)
            .Automation(new() { AutomationId = "skin.palette.cancel" }),
    ]);
}

internal sealed partial class SkinPaletteMore(SkinPaletteDialog dialog) : Control
{
    private Ui Body => Row([
        Button(
            Row([
                LauncherIcons.Line("more", 16f)
            ]),
            () => dialog.ShowMenu())
            .Style(SkinEditorChrome.QuietIconCommand)
            .Width(28f).Height(28f)
            .ToolTip(Resources.SkinPaletteManage)
            .Automation(new()
            {
                Name = Resources.SkinPaletteManage,
                AutomationId = "skin.palette.actions"
            })
    ]);
}

internal sealed partial class SkinPaletteNameInput(SkinPaletteDialog dialog, bool inline) : Control
{
    private Ui Body => TextBox(dialog.Name, value =>
        {
            dialog.Name = value;
            dialog.Error = string.Empty;
        })
        .Style(LauncherStyles.Input)
        .MinHeight(34f)
        .SubmitOnEnter()
        .OnSubmitted(_ =>
        {
            if (inline)
                dialog.CommitName();
            else
                dialog.Confirm();
        })
        .OnLostFocus(() =>
        {
            if (inline)
                dialog.CommitName();
        })
        .Automation(new()
        {
            Name = Resources.SkinPaletteName,
            AutomationId = inline ? "skin.palette.rename.input" : "skin.palette.name"
        });
}

internal sealed partial class SkinPaletteFields(SkinPaletteDialog dialog, SkinPaletteNameInput name) : Control
{
    private readonly SkinPaletteNameInput _name = name;
    private readonly SkinPaletteReferenceView _reference = dialog.Reference;
    private Func<int, IFlyout>? Picker => dialog.ReadOnly || !dialog.Palettes.CanWrite ? null : dialog.ColorPicker;

    private Ui Body => Column([
        Column([
            Text(Resources.SkinPaletteName)
                .FontSize(12f),
            _name,
        ])
            .Spacing(6f)
            .Visible(dialog.Creating),
        _reference
            .Visible(dialog.Mode == SkinPaletteMode.Reference),
        Column([
            new SkinPaletteSwatches(
LauncherAppearance.Capture(),                dialog.Colors,
                dialog.Foreground,
                dialog.Selected,
                dialog.ContentWidth,
                dialog.CanAdd,
                dialog.ReadOnly ? Resources.SkinPaletteCopyAdd : Resources.SkinPaletteAdd,
                "skin.palette.editor.swatch.",
                dialog.Choose,
                dialog.Add,
                Picker),
            Text(Resources.SkinPaletteReadOnly)
                .FontSize(11f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Visible(dialog.ReadOnly),
        ])
            .Spacing(12f)
            .Visible(dialog.Mode != SkinPaletteMode.Delete),
        Text(string.Format(Resources.SkinPaletteDeleteConfirm, SkinPaletteController.DisplayName(dialog.Palettes.Current)))
            .FontSize(13f)
            .Wrap()
            .Visible(dialog.Mode == SkinPaletteMode.Delete),
        Text(dialog.Error)
            .FontSize(11f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(dialog.Error.Length > 0)
            .Automation(new() { AutomationId = "skin.palette.error" }),
        Text(dialog.Palettes.Error)
            .FontSize(11f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(dialog.Palettes.Error.Length > 0),
        LauncherControls.OutlineCommand(Resources.SkinPaletteReload, dialog.Palettes.Reload)
            .Style(LauncherButtons.DialogHeaderButton)
            .Visible(!dialog.Palettes.CanWrite)
            .Automation(new() { AutomationId = "skin.palette.reload" }),
    ])
        .Spacing(12f);
}