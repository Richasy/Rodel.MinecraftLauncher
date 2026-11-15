// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Semantics;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

/// <summary>Hosts the real setting controls in the shared native card surface, retaining their input and automation trees.</summary>
internal sealed partial class LauncherSettingsCard : Control
{
    private readonly Window? _window;
    private readonly SettingsCard _card;
    private readonly Func<string>? _localizedHeader;
    private readonly Control _content;
    private readonly float _groupedLeadingInset;

    internal LauncherSettingsCard(
        string title,
        Control content,
        Window? window = null,
        Func<string>? localizedHeader = null,
        float groupedLeadingInset = 16f)
    {
        _groupedLeadingInset = groupedLeadingInset;
        _window = window;
        _content = content;
        _localizedHeader = localizedHeader;
        _card = new SettingsCard(title, content).ContentAlignment(SettingsCardContentAlignment.Left).Automation(new() { Name = title });
    }

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is LauncherSettingsCard card)
            _card.ReconcileFrom(card._card);
    }

    private SettingsCardStyle Appearance
    {
        get
        {
            if (_localizedHeader is not null)
            {
                var title = _localizedHeader();
                _card.ReconcileFrom(new SettingsCard(title, _content).ContentAlignment(SettingsCardContentAlignment.Left));
                _card.Automation(new() { Name = title });
            }

            return default(SettingsCardStyle).Palette(new(LauncherButtonPalette.Create(false, false), LauncherTheme.Muted));
        }
    }

    private Ui Body => _card.Style(Appearance);

    private void Square()
    {
        if (Render is { } render)
            SquareBorders(render, _groupedLeadingInset);
    }

    internal static void SquareBorders(Widget widget, float groupedLeadingInset = 16f)
    {
        if (widget is BorderWidget border)
        {
            border.SetCornerRadius(CornerRadius.Zero);
            if (border.Padding == new Thickness(58f, 8f, 44f, 8f))
                border.SetPadding(new Thickness(groupedLeadingInset, 16f, 16f, 16f));
        }

        widget.VisitChildren(child => SquareBorders(child, groupedLeadingInset));
    }

    protected override IDisposable? OnMounted()
    {
        _window?.LayoutUpdated += Square;
        Square();
        return null;
    }

    protected override void OnDispose()
    {
        _window?.LayoutUpdated -= Square;
        base.OnDispose();
    }
}