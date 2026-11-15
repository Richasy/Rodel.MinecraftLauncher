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

/// <summary>Uses the native settings expander and its interactive slots with the launcher's square geometry.</summary>
internal sealed partial class LauncherSettingsGroup : Control
{
    private readonly SettingsExpander _expander;
    private readonly Func<string>? _localizedHeader;
    private readonly Func<string>? _localizedDescription;
    private readonly Func<SettingsExpanderItem[]>? _items;
    private readonly Control? _body;
    private readonly Control? _footer;
    private readonly Control? _commands;
    private readonly IconElement? _artwork;
    private readonly string _title;
    private readonly string _icon;
    private readonly string? _automationId;
    private string _description;
    private readonly Window? _window;

    internal LauncherSettingsGroup(
        string title,
        string description,
        string icon,
        Control? body,
        Control? footer,
        Control? commands = null,
        bool expanded = false,
        Window? window = null,
        string? automationId = null,
        IconElement? artwork = null,
        Func<string>? localizedHeader = null,
        Func<string>? localizedDescription = null,
        Func<SettingsExpanderItem[]>? items = null)
    {
        _window = window;
        _title = title;
        _description = description;
        _localizedHeader = localizedHeader;
        _localizedDescription = localizedDescription;
        _body = body;
        _icon = icon;
        _footer = footer;
        _commands = commands;
        _artwork = artwork;
        _automationId = automationId;
        _items = items;
        _expander = Create(expanded);
    }

    private SettingsExpander Create(bool expanded)
    {
        var title = _localizedHeader?.Invoke() ?? _title;
        var next = SettingsExpander(title).Description(_localizedDescription?.Invoke() ?? _description).Expanded(expanded);
        if (_items is not null)
            ApplyItems(next);
        else
            next.Items(_body is null ? [] : [_body]);
        if (_artwork is not null)
            next.Icon(_artwork);
        else if (_icon.Length > 0)
            next.Icon(LauncherIcons.Element(_icon));
        if (_footer is not null)
            next.ItemsFooter(_footer);
        if (_commands is not null)
            next.Content(_commands);
        if (_automationId is not null)
        {
            next.Automation(new()
            {
                Name = title,
                AutomationId = _automationId
            });
        }

        return next;
    }

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is LauncherSettingsGroup group)
        {
            group._expander.Expanded(_expander.IsExpanded);
            group._expander.Style(Appearance);
            _expander.ReconcileFrom(group._expander);
        }
    }

    internal void Expand() => _expander.Expanded(true);

    private void ApplyItems(SettingsExpander expander)
    {
        var items = _items!();
        if (items.Length == 0)
            expander.Items();
        else
            expander.Items(items[0], items[1..]);
    }

    internal void RefreshItems()
    {
        if (_items is not null)
            ApplyItems(_expander);
    }

    internal void SetSummary(string summary)
    {
        _description = summary;
        _expander.Description(summary);
    }

    private ExpanderStyle Appearance
    {
        get
        {
            var appearance = default(ExpanderStyle).Palette(new(
                    LauncherButtonPalette.Create(false, false) with { Pressed = LauncherButtonPalette.Create(false, false).Pressed with { Border = LauncherTheme.Edge } },
                    LauncherButtonPalette.Create(false, false),
                    LauncherTheme.Edge,
                    LauncherTheme.ControlBackground,
                    LauncherTheme.Edge));
            if (_localizedHeader is not null)
            {
                _expander.ReconcileFrom(Create(_expander.IsExpanded).Style(appearance));
                if (_automationId is not null)
                {
                    _expander.Automation(new()
                    {
                        Name = _localizedHeader(),
                        AutomationId = _automationId
                    });
                }
            }

            return appearance;
        }
    }

    private Ui Body => _expander.Style(Appearance);

    private void UpdateSquare()
    {
        if (Render is { } render)
            Square(render);
    }

    protected override IDisposable? OnMounted()
    {
        if (_window is { } window)
            window.LayoutUpdated += UpdateSquare;
        UpdateSquare();
        return null;
    }

    protected override void OnDispose()
    {
        if (_window is { } window)
            window.LayoutUpdated -= UpdateSquare;
        base.OnDispose();
    }

    private static void Square(Widget widget)
    {
        if (widget is BorderWidget border)
        {
            border.SetCornerRadius(CornerRadius.Zero);
            // Sprout's settings header reserves only 4 DIP on the trailing edge. Balance that owned header chrome
            // without changing its native toggle, keyboard handling or interactive content slots.
            if (border.Padding == new Thickness(16f, 16f, 4f, 16f))
                border.SetPadding(16f);
        }

        widget.VisitChildren(Square);
    }
}