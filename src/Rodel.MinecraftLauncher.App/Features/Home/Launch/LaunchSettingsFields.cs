// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Styling;
using System.Globalization;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Home;

[View]
internal readonly partial record struct LaunchSettingsFields(
    LauncherAppearance Theme,
    Window Window,
    LaunchSettingsDraft Draft,
    Action<Func<LaunchSettingsDraft, LaunchSettingsDraft>> Changed,
    Color Ink,
    string AutomationPrefix = "launch.settings",
    Thickness ContentInset = default)
{
    internal void RestoreVersion() => Changed(draft => draft with
    {
        Version = draft.RepairVersion ?? draft.Version,
        RepairVersionSelected = true,
        Error = string.Empty
    });
    internal Action<string> ChangeJava
    {
        get
        {
            var changed = Changed;
            return value => changed(draft => draft with { Java = value });
        }
    }

    internal void ChangeMemory(NumberBoxValueChangedEventArgs value) => Changed(draft => draft with
    {
        Memory = double.IsNaN(value.NewValue) ? string.Empty : value.NewValue.ToString("R", CultureInfo.InvariantCulture),
        MemoryEditing = false,
        Error = string.Empty
    });

    internal void ChangeMemoryMode(int index) => Changed(draft => draft with { AutomaticMemory = index == 1 });

    internal void ChangeMemoryText(string value) => Changed(draft => draft with
    {
        // Keep the native committed value stable while typing so draft validation cannot reset the caret.
        MemoryEditValue = draft.MemoryEditing ? draft.MemoryEditValue : draft.MemoryValue,
        MemoryEditing = true,
        Memory = double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var number) ? number.ToString("R", CultureInfo.InvariantCulture) : value,
        Error = string.Empty,
    });

    internal void EndMemoryEdit() => Changed(draft => draft with { MemoryEditing = false });

    internal void ChangePlayer(string value) => Changed(draft => draft with { Player = value, PlayerEditing = true });
    internal void EndPlayerEdit() => Changed(draft => draft with { PlayerEditing = false });

    internal void ChangeGameBridge(int index)
    {
        if (index is < 0 or > 2)
            return;
        Changed(draft => draft with
        {
            GameBridge = (GameBridgeSettingMode)index,
            Error = string.Empty
        });
    }

    internal string[] GameBridgeChoices => [Draft.GlobalGameBridge ? Resources.BridgeInheritEnabled : Resources.BridgeInheritDisabled, Resources.SettingsDisabled, Resources.SettingsEnabled];

    public Ui Body => Column([
        Text(Resources.LaunchSettingsExplanation)
            .FontSize(12f).Foreground(Theme.Muted)
            .Wrap(),
        Text(Resources.LaunchVersion)
            .FontSize(12f).Foreground(Ink),
        TextBox()
            .Text(Draft.Version)
            .MinHeight(LauncherStyles.InputHeight)
            .ReadOnly()
            .CornerRadius(0f)
            .Automation(new()
            {
                Name = Resources.LaunchVersion,
                AutomationId = AutomationPrefix + ".version",
            }),
        Text(Resources.LaunchVersionReadOnlyHelp)
            .FontSize(12f).Foreground(Theme.Muted)
            .Wrap(),
        Text(Resources.LaunchVersionUnavailable)
            .FontSize(12f).Foreground(Theme.Warning)
            .Wrap()
            .Visible(Draft.VersionUnavailable && !Draft.RepairVersionSelected),
        LauncherControls.OutlineCommand(Theme,string.Format(Resources.LaunchRepairVersion, Draft.RepairVersion), RestoreVersion)
            .Enabled(!Draft.RepairVersionSelected)
            .Visible(Draft.RepairVersion is not null)
            .Automation(new()
            {
                Name = string.Format(Resources.LaunchRepairVersion, Draft.RepairVersion),
                AutomationId = AutomationPrefix + ".repair-version"
            }),
        Text(Resources.LaunchJavaPath)
            .FontSize(12f).Foreground(Ink),
        new FileSystemPathInput(
            Window,
            Draft.Java,
            ChangeJava,
            Resources.LaunchJavaPath,
            AutomationPrefix + ".java",
            Extensions: [".exe"],
            Clearable: true,
            Placeholder: Resources.JavaAutomatic),
        Text(Resources.SettingsMemoryMode)
            .FontSize(12f).Foreground(Ink),
        LauncherControls.Choice(Theme,[Resources.SettingsMemoryManual, Resources.SettingsMemoryAutomatic])
            .SelectedIndex(Draft.AutomaticMemory ? 1 : 0)
            .OnSelectionChanged(ChangeMemoryMode)
            .MinHeight(40f)
            .Automation(new()
            {
                Name = Resources.SettingsMemoryMode,
                AutomationId = AutomationPrefix + ".memory-mode"
            }),
        Text(Resources.LaunchMemory)
            .FontSize(12f).Foreground(Ink)
            .Visible(!Draft.AutomaticMemory),
        new LauncherNumberBox(ChangeMemoryText).Value(Draft.MemoryEditorValue)
            .OnValueChanged(ChangeMemory)
            .ValidationMode(NumberBoxValidationMode.Disabled)
            .AcceptsOnlyNumericInput()
            .SmallChange(256d).LargeChange(1024d)
            .SpinButtonPlacement(NumberBoxSpinButtonPlacementMode.Inline)
            .OnLostFocus(EndMemoryEdit)
            .Style(Theme.Styles.NumberInput)
            .Visible(!Draft.AutomaticMemory)
            .Automation(new()
            {
                Name = Resources.LaunchMemory,
                AutomationId = AutomationPrefix + ".memory",
            }),
        Text(Draft.MemoryPolicy)
            .FontSize(12f).Foreground(Theme.Muted)
            .Wrap()
            .Visible(!Draft.AutomaticMemory),
        Text(Draft.ValidationError)
            .FontSize(12f).Foreground(Theme.Error)
            .Wrap()
            .Visible(Draft.ValidationError.Length > 0)
            .Automation(new()
            {
                Name = Draft.ValidationError,
                AutomationId = AutomationPrefix + ".validation",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
        Text(Draft.MemoryWarning)
            .FontSize(12f).Foreground(Theme.Warning)
            .Wrap()
            .Visible(Draft.MemoryWarning.Length > 0),
        Text(Resources.LaunchOfflineName)
            .FontSize(12f).Foreground(Ink),
        TextBox(Draft.Player, ChangePlayer)
            .MinHeight(LauncherStyles.InputHeight)
            .OnLostFocus(EndPlayerEdit)
            .CornerRadius(0f)
            .Automation(new()
            {
                Name = Resources.LaunchOfflineName,
                AutomationId = AutomationPrefix + ".player",
            }),
        Text(Resources.BridgeTitle)
            .FontSize(12f).Foreground(Ink),
        LauncherControls.Choice(Theme,GameBridgeChoices)
            .SelectedIndex((int)Draft.GameBridge)
            .OnSelectionChanged(ChangeGameBridge)
            .MinHeight(40f)
            .Automation(new()
            {
                Name = Resources.BridgeTitle,
                AutomationId = AutomationPrefix + ".gameBridgeEnabled"
            }),
        Text(Resources.BridgeSettingsHelp)
            .FontSize(12f).Foreground(Theme.Muted)
            .Wrap(),
        Text(Draft.Error)
            .FontSize(12f).Foreground(Theme.Error)
            .Wrap()
            .Visible(Draft.Error.Length > 0),
    ])
        .Spacing(8f)
        .Padding(ContentInset);
}