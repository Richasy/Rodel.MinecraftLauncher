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

internal sealed partial class CorePreferencesForm : Control
{
    internal readonly CorePreferencesStore _store;
    private readonly string _category;
    private readonly PreferenceFieldsBody _fieldsBody;

    internal CorePreferencesForm(
        CorePreferencesStore store,
        string category,
        Window window,
        Action? openJava = null,
        DiagnosticsSection? diagnostics = null)
    {
        _store = store;
        _category = category;
        _fieldsBody = new(
            CorePreferenceFields.For(category),
            store,
            window,
            openJava,
            diagnostics);
    }

    internal bool RealizeSearchTarget(string id) => _fieldsBody.Realize(id);
    internal CorePreferencesState State => _store.State;
    private string Validation => _store.Validation(_category, includeProxy: false);
    private string SaveError => _category == "network" && State.Error == _store.ProxyValidation ? string.Empty : State.Error;

    private Ui Body => Column([
        Grid(
            [GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                Text(Resources.SettingsDefaultsScope)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .MaxLines(1).Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                    .Visible(_category != "network")
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                LauncherControls.OutlineCommand(Resources.SettingsResetCategory, () => _store.Reset(_category))
                    .MinHeight(38f)
                    .Enabled(State.Loaded && !State.Busy)
                    .Visible(State.CanReset(_category))
                    .Automation(new()
                    {
                        Name = Resources.SettingsResetCategory,
                        AutomationId = "settings." + _category + ".reset"
                    })
                    .Cell(1, 0),
            ])
            .ColumnSpacing(12f),
        _fieldsBody.Automation(new() { AutomationId = "settings." + _category + ".fields" }),
        Column([
            Text(Validation)
                .FontSize(12f).Foreground(LauncherTheme.Error)
                .Wrap()
                .Visible(Validation.Length > 0)
                .Automation(new()
                {
                    Name = Validation,
                    AutomationId = "settings." + _category + ".validation",
                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                }),
            Text(SaveError)
                .FontSize(12f).Foreground(LauncherTheme.Error)
                .Wrap()
                .Visible(SaveError.Length > 0)
                .Automation(new()
                {
                    Name = SaveError,
                    AutomationId = "settings." + _category + ".error",
                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                }),
        ])
            .Spacing(8f)
            .Visible(Validation.Length > 0 || SaveError.Length > 0),
    ])
        .Spacing(12f);
}

/// <summary>Keeps this small fixed form mounted and opens the native ancestor group for search.</summary>
internal sealed partial class PreferenceFieldsBody : Control
{
    private readonly Dictionary<string, LauncherSettingsGroup> _groups = [];
    private readonly SettingsRows _rows;
    private readonly CorePreferencesStore _store;

    internal PreferenceFieldsBody(
        PreferenceField[] fields,
        CorePreferencesStore store,
        Window window,
        Action? openJava = null,
        DiagnosticsSection? diagnostics = null)
    {
        _store = store;
        var controls = fields.ToDictionary(field => field.Key, field => new PreferenceFieldControl(field, store, window));
        var rows = new List<Control>();
        if (openJava is not null)
            rows.Add(new JavaManagerSetting(openJava, window));
        void Group(
            Func<string> title,
            Func<string> description,
            string[] keys,
            bool expanded = false,
            string? automationId = null)
        {
            SettingsExpanderItem[] Items() => [.. keys.Select(key => new SettingsExpanderItem(controls[key], isVisible: controls[key].Visible))];
            var group = new LauncherSettingsGroup(
                title(),
                description(),
                string.Empty,
                null,
                null,
                expanded: expanded,
                window: window,
                automationId: automationId,
                localizedHeader: title,
                localizedDescription: description,
                items: Items);
            foreach (var key in keys)
                _groups[key] = group;
            rows.Add(group);
        }

        switch (fields.FirstOrDefault()?.Category)
        {
            case "launch":
                Group(() => Resources.SettingsGameWindowGroup, () => Resources.SettingsGameWindowHelp, ["fullscreen", "width", "height"], true);
                rows.Add(controls["quickPlaySingleplayer"]);
                rows.Add(controls["gameBridgeEnabled"]);
                break;
            case "java":
                rows.Add(controls["javaPath"]);
                Group(() => Resources.SettingsMemoryGroup, () => Resources.SettingsMemoryHelp, ["memoryMode", "maxMemoryMb"], true);
                Group(() => Resources.SettingsAdvanced, () => string.Empty, ["minMemoryMb", "memoryReserveMb", "autoMemoryMinMb", "autoMemoryMaxMb"], automationId: "settings.java.memory-advanced");
                break;
            case "network":
                rows.Add(controls["network.source"]);
                rows.Add(controls["network.proxy"]);
                rows.Add(new ProxyAddressSetting(store, window));
                rows.Add(controls["network.concurrency"]);
                Group(() => Resources.SettingsTimeoutGroup, () => Resources.SettingsTimeoutHelp, [.. fields.Where(field => field.Key.EndsWith("Seconds", StringComparison.Ordinal)).Select(field => field.Key)]);
                Group(() => Resources.SettingsMirrors, () => Resources.SettingsMirrorsHelp, ["network.mirrors"]);
                break;
            case "logs":
                rows.Add(controls["loggingCompatibility"]);
                Group(() => Resources.SettingsEncodingGroup, () => Resources.SettingsEncodingGroupHelp, ["stdoutEncoding", "stderrEncoding"]);
                break;
            case "advanced":
                foreach (var field in fields)
                    Group(field.Title, field.Description ?? (() => string.Empty), [field.Key]);
                break;
        }

        if (diagnostics is not null)
            rows.Add(new LauncherSettingsCard(Resources.DiagnosticsGroup, diagnostics, window, () => Resources.DiagnosticsGroup));
        _rows = new([.. rows]);
    }

    private Ui Body => Border(_rows);

    private void RefreshConditionalGroups()
    {
        _ = _store.State;
        Untracked.Run(() =>
            {
                foreach (var group in _groups.Values.Distinct())
                    group.RefreshItems();
            });
    }

    protected override IDisposable? OnMounted() => new Effect(RefreshConditionalGroups);

    internal bool Realize(string id)
    {
        var key = id.StartsWith("settings.number.", StringComparison.Ordinal)
            ? id[16..]
            : id.StartsWith("settings.input.", StringComparison.Ordinal)
            ? id[15..]
            : id.StartsWith("settings.field.", StringComparison.Ordinal) ? id[15..] : string.Empty;
        if (!_groups.TryGetValue(key, out var group))
            return false;
        group.Expand();
        return true;
    }
}