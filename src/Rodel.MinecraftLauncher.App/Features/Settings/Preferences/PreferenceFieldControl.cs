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

internal sealed partial class PreferenceFieldControl : Control
{
    internal partial PreferenceField Field { get; set; }
    internal readonly CorePreferencesStore _store;
    internal readonly Window _window;

    internal PreferenceFieldControl(PreferenceField field, CorePreferencesStore store, Window window)
    {
        Field = field;
        _store = store;
        _window = window;
        Compact = window.ClientSize.Width < 1100f;
        _editor = new(this);
    }

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is PreferenceFieldControl other)
        {
            Field = other.Field;
        }
    }

    internal CorePreferencesState State => _store.State;
    internal partial bool Compact { get; set; }
    internal string Value => State.Text(Field);
    private bool AutoMemory => State.Text(CorePreferenceFields.All.First(preference => preference.Key == "memoryMode")) == "auto";
    internal bool Visible => Field.Key == "maxMemoryMb"
        ? !AutoMemory
        : AutoMemory || Field.Key is not ("memoryReserveMb" or "autoMemoryMinMb" or "autoMemoryMaxMb");
    internal string Title => Field.Title();
    internal string Description => Field.Key == "minMemoryMb"
        ? Resources.SettingsInitialMemoryHelp
        : Field.Key == "javaPath" && State.StoredValue("javaRuntimeId") is not null && !State.Removed.Contains("javaPath") ? Resources.SettingsManagedJavaHelp : Field.Description?.Invoke() ?? string.Empty;
    internal bool Enabled => State.Loaded;
    internal bool Multiline => Field.Kind is PreferenceKind.Arguments or PreferenceKind.Environment or PreferenceKind.Mirrors;
    internal bool Choice => Field.Kind is PreferenceKind.MemoryMode or PreferenceKind.Proxy or PreferenceKind.DownloadSource;
    internal static string[] EncodingChoices => [Resources.SettingsEncodingAutomatic, "UTF-8", "GB18030", "GBK", "Windows-1252", "UTF-16"];
    private static readonly string[] EncodingValues = [string.Empty, "utf-8", "gb18030", "gbk", "windows-1252", "utf-16"];
    internal int EncodingIndex => Array.FindIndex(EncodingValues, value => string.Equals(value, Value, StringComparison.OrdinalIgnoreCase));

    internal void ChooseEncoding(int index)
    {
        if (index >= 0 && index < EncodingValues.Length)
            _store.Set(Field, EncodingValues[index]);
    }

    internal void EditEncoding(string value)
    {
        if (value != Resources.SettingsEncodingAutomatic)
            _store.Set(Field, value);
    }

    internal string[] Choices => Field.Kind switch
    {
        PreferenceKind.MemoryMode => [Resources.SettingsMemoryManual, Resources.SettingsMemoryAutomatic],
        PreferenceKind.DownloadSource => [Resources.SettingsDownloadSourceOfficial, Resources.SettingsDownloadSourceBmclapi],
        _ => [Resources.SettingsProxySystem, Resources.SettingsProxyDirect, Resources.SettingsProxyCustom],
    };
    internal int Selected => Field.Kind == PreferenceKind.DownloadSource
        ? Value == "bmclapi" ? 1 : 0
        : Field.Kind == PreferenceKind.MemoryMode
        ? Value == "auto"
        ? 1
        : Value == "manual" ? 0 : -1
        : State.ProxyManual
        ? 2
        : Value == "direct" ? 1 : 0;
    internal double Number => double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : double.NaN;

    internal void Choose(int index)
    {
        if (index < 0 || !Choice || index == Selected)
            return;
        if (Field.Kind == PreferenceKind.Proxy)
            _store.SetProxyMode(index);
        else if (Field.Kind == PreferenceKind.DownloadSource)
            _store.Set(Field, index == 1 ? "bmclapi" : "official");
        else
            _store.Set(Field, index == 1 ? "auto" : "manual");
    }

    internal void ChangeNumber(NumberBoxValueChangedEventArgs value)
    {
        if (Field.Kind != PreferenceKind.Integer)
            return;
        _store.SetNumber(Field, value.NewValue);
        _store.SetEditing(Field.Key, false);
    }
    private readonly PreferenceEditorContent _editor;

    private Ui Body => Column([
        new LauncherSettingsCard(Title, _editor, _window).Visible(Visible)
    ]);

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs args) => Compact = args.Size.Width < 1100f;

    protected override IDisposable? OnMounted()
    {
        _window.SizeChanged += OnSizeChanged;
        return null;
    }

    protected override void OnDispose()
    {
        _window.SizeChanged -= OnSizeChanged;
        base.OnDispose();
    }
}