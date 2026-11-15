// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

/// <summary>Keeps the manual proxy editor and its validation in a separately collapsible settings card.</summary>
internal sealed partial class ProxyAddressSetting : Control
{
    internal readonly CorePreferencesStore Store;
    internal readonly PreferenceField Field = CorePreferenceFields.All.First(field => field.Kind == PreferenceKind.Proxy);
    private readonly LauncherSettingsCard _card;

    internal ProxyAddressSetting(CorePreferencesStore store, Window window)
    {
        Store = store;
        Compact = window.ClientSize.Width < 1100f;
        _window = window;
        _card = new(Resources.SettingsProxyAddress, new ProxyAddressContent(this), window, () => Resources.SettingsProxyAddress);
    }
    private readonly Window _window;
    internal CorePreferencesState State => Store.State;
    internal partial bool Compact { get; private set; }
    internal string Error => Store.ProxyValidation;

    private Ui Body => Column([
        _card.Visible(State.ProxyManual)
    ]);

    private void Resize(object? sender, WindowSizeChangedEventArgs args) => Compact = args.Size.Width < 1100f;

    protected override IDisposable? OnMounted()
    {
        _window.SizeChanged += Resize;
        return null;
    }

    protected override void OnDispose()
    {
        _window.SizeChanged -= Resize;
        base.OnDispose();
    }
}

internal sealed partial class ProxyAddressContent(ProxyAddressSetting owner) : Control
{
    private Ui Body => Grid(
        owner.Compact ? [GridLength.Star()] : [GridLength.Star(), GridLength.Pixels(300f)],
        owner.Compact ? [GridLength.Auto, GridLength.Auto] : [GridLength.Auto],
        [
            Column([
                Text(Resources.SettingsProxyAddress)
                    .FontSize(14f).Foreground(LauncherTheme.Ink),
                Text(Resources.SettingsProxyAddressHelp)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Wrap(),
            ])
                .Spacing(6f)
                .VAlign(VerticalAlignment.Center)
                .Cell(0, 0),
            Column([
                TextBox(owner.State.Text(owner.Field), value => owner.Store.Set(owner.Field, value))
                    .Placeholder(Resources.SettingsProxyAddressPlaceholder)
                    .MinHeight(LauncherStyles.InputHeight)
                    .Style(LauncherStyles.Input)
                    .Enabled(owner.State.Loaded)
                    .Automation(new()
                    {
                        Name = Resources.SettingsProxyAddress,
                        AutomationId = "settings.input.network.proxy"
                    }),
                Text(owner.Error)
                    .FontSize(12f).Foreground(LauncherTheme.Error)
                    .Wrap()
                    .Visible(owner.Error.Length > 0)
                    .Automation(new()
                    {
                        Name = owner.Error,
                        AutomationId = "settings.proxy.address.error",
                        LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                    }),
            ])
                .Spacing(6f)
                .VAlign(VerticalAlignment.Center)
                .Cell(owner.Compact ? 0 : 1, owner.Compact ? 1 : 0),
        ])
        .ColumnSpacing(24f).RowSpacing(10f);
}