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

internal sealed partial class JavaManagerSetting : Control
{
    private readonly SettingsCard _card;
    private readonly Window _window;
    private readonly Action _open;

    internal JavaManagerSetting(Action open, Window window)
    {
        _window = window;
        _open = open;
        _card = SettingsCard(Resources.SettingsJavaManagerTitle).OnClick(open).Automation(new()
        {
            Name = Resources.SettingsJavaManagerTitle,
            AutomationId = "settings.java.manager"
        });
    }

    private SettingsCardStyle Appearance
    {
        get
        {
            _card.ReconcileFrom(SettingsCard(Resources.SettingsJavaManagerTitle).Description(Resources.SettingsJavaManagerHelp).OnClick(_open).Automation(new()
            {
                Name = Resources.SettingsJavaManagerTitle,
                AutomationId = "settings.java.manager"
            }));
            _card.Automation(new()
            {
                Name = Resources.SettingsJavaManagerTitle,
                AutomationId = "settings.java.manager"
            });
            return default(SettingsCardStyle).Palette(new(LauncherButtonPalette.Create(false, false), LauncherTheme.Muted));
        }
    }

    private Ui Body => _card.Style(Appearance);

    private void Square()
    {
        if (Render is { } render)
            LauncherSettingsCard.SquareBorders(render);
    }

    protected override IDisposable? OnMounted()
    {
        _window.LayoutUpdated += Square;
        return null;
    }

    protected override void OnDispose()
    {
        _window.LayoutUpdated -= Square;
        base.OnDispose();
    }
}