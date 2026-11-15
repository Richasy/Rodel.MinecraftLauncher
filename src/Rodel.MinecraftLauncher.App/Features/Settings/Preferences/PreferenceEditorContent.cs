// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reactive;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using System.Globalization;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed partial class PreferenceEditorContent : Control
{
    private readonly PreferenceFieldControl _owner;
    private readonly SettingsRows _input;

    internal PreferenceEditorContent(PreferenceFieldControl owner)
    {
        _owner = owner;
        Control input = owner.Field.Key switch
        {
            "stdoutEncoding" or "stderrEncoding" => new EncodingPreferenceInput(owner),
            "javaPath" => new JavaPathPreferenceInput(owner),
            _ => owner.Field.Kind switch
            {
                PreferenceKind.Boolean => new BooleanPreferenceInput(owner),
                PreferenceKind.Integer => new NumberPreferenceInput(owner),
                PreferenceKind.MemoryMode or PreferenceKind.Proxy or PreferenceKind.DownloadSource => new ChoicePreferenceInput(owner),
                _ => new TextPreferenceInput(owner),
            },
        };
        _input = new([input], 0f);
    }

    private Ui Body => Grid(
        _owner.Compact ? [GridLength.Star()] : [GridLength.Star(), GridLength.Pixels(300f)],
        _owner.Compact ? [GridLength.Auto, GridLength.Auto] : [GridLength.Auto],
        [
            Column([
                Text(_owner.Title)
                    .FontSize(14f).Foreground(LauncherTheme.Ink)
                    .Wrap()
                    .Visible(_owner.Field.Category != "advanced"),
                Text(_owner.Description)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Wrap()
                    .Visible(_owner.Description.Length > 0 && _owner.Field.Category != "advanced"),
            ])
                .Spacing(6f)
                .VAlign(VerticalAlignment.Center)
                .Cell(0, 0),
            _input.VAlign(VerticalAlignment.Center)
                .Cell(_owner.Compact ? 0 : 1, _owner.Compact ? 1 : 0),
        ])
        .ColumnSpacing(24f).RowSpacing(10f);
}

internal sealed partial class BooleanPreferenceInput(PreferenceFieldControl owner) : Control
{
    private bool Value => bool.TryParse(owner.Value, out var enabled) && enabled;

    private Ui Body => ToggleSwitch(Value, value => owner._store.Set(owner.Field, value ? "true" : "false"))
        .NoStatusText()
        .Palette(LauncherStyles.ContentToggle)
        .OnSurfacePaint(LauncherToggleSurface.Paint)
        .Enabled(owner.Enabled)
        .HAlign(HorizontalAlignment.Right)
        .Automation(new()
        {
            Name = owner.Title,
            AutomationId = "settings.field." + owner.Field.Key
        });
}

internal sealed partial class NumberPreferenceInput(PreferenceFieldControl owner) : Control
{
    private Ui Body => new LauncherNumberBox().Value(owner.Number)
        .ValidationMode(NumberBoxValidationMode.Disabled)
        .AcceptsOnlyNumericInput()
        .OnValueChanged(owner.ChangeNumber)
        .OnGotFocus(_ => owner._store.SetEditing(owner.Field.Key, true))
        .OnLostFocus(() => owner._store.SetEditing(owner.Field.Key, false))
        .SmallChange(1d).LargeChange(10d)
        .SpinButtonPlacement(NumberBoxSpinButtonPlacementMode.Inline)
        .Style(LauncherStyles.NumberInput)
        .Enabled(owner.Enabled)
        .Automation(new()
        {
            Name = owner.Title,
            AutomationId = "settings.number." + owner.Field.Key
        });
}

internal sealed partial class EncodingPreferenceInput(PreferenceFieldControl owner) : Control
{
    private Ui Body => LauncherControls.Choice(PreferenceFieldControl.EncodingChoices)
        .IsEditable(true)
        .Text(owner.Value)
        .Placeholder(Resources.SettingsEncodingAutomatic)
        .SelectedIndex(owner.EncodingIndex)
        .OnTextChanged(owner.EditEncoding)
        .OnSelectionChanged(owner.ChooseEncoding)
        .Enabled(owner.Enabled)
        .MinHeight(40f)
        .Automation(new()
        {
            Name = owner.Title,
            AutomationId = "settings.input." + owner.Field.Key
        });
}

internal sealed partial class ChoicePreferenceInput(PreferenceFieldControl owner) : Control
{
    private Ui Body => LauncherControls.Choice(owner.Choices)
        .SelectedIndex(owner.Selected)
        .OnSelectionChanged(owner.Choose)
        .Enabled(owner.Enabled)
        .MinHeight(40f)
        .Automation(new()
        {
            Name = owner.Title,
            AutomationId = "settings.field." + owner.Field.Key
        });
}

internal sealed partial class TextPreferenceInput(PreferenceFieldControl owner) : Control
{
    private Ui Body => TextBox(owner.Value, value => owner._store.Set(owner.Field, value))
        .AcceptsReturn(owner.Multiline)
        .MinHeight(owner.Multiline ? 120f : LauncherStyles.InputHeight)
        .Style(LauncherStyles.Input)
        .Enabled(owner.Enabled)
        .Automation(new()
        {
            Name = owner.Title,
            AutomationId = "settings.input." + owner.Field.Key
        });
}

internal sealed partial class JavaPathPreferenceInput(PreferenceFieldControl owner) : Control
{
    private Ui Body => new FileSystemPathInput(
        owner._window,
        owner.Value,
        value => owner._store.Set(owner.Field, value),
        owner.Title,
        "settings.input.javaPath",
        Enabled: owner.Enabled,
        Extensions: [".exe"],
        Clearable: true,
        BrowseId: "settings.java.file",
        Placeholder: Resources.JavaAutomatic);
}