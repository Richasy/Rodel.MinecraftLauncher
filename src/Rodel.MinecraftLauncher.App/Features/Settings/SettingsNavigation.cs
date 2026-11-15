// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Wallpapers;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Theming;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed partial class SettingsNavigation : Control
{
    private readonly Action<string> _select;

    internal SettingsNavigation(Action<string> select)
    {
        _select = select;
        Category = "general";
    }

    internal partial string Category { get; set; }

    private Ui Body => LauncherControls.KeyboardGroup(Column([
        new SettingsNavigationItem(
LauncherAppearance.Capture(),            Resources.SettingsLauncher,
            "general",
            Category,
            () => _select("general"),
            LauncherButtons.Navigation(Category == "general")),
        new SettingsNavigationItem(
LauncherAppearance.Capture(),            Resources.SettingsGameLaunch,
            "launch",
            Category,
            () => _select("launch"),
            LauncherButtons.Navigation(Category == "launch")),
        new SettingsNavigationItem(
LauncherAppearance.Capture(),            Resources.SettingsJavaMemory,
            "java",
            Category,
            () => _select("java"),
            LauncherButtons.Navigation(Category == "java")),
        new SettingsNavigationItem(
LauncherAppearance.Capture(),            Resources.SettingsNetwork,
            "network",
            Category,
            () => _select("network"),
            LauncherButtons.Navigation(Category == "network")),
        new SettingsNavigationItem(
LauncherAppearance.Capture(),            Resources.SettingsContentCache,
            "content",
            Category,
            () => _select("content"),
            LauncherButtons.Navigation(Category == "content")),
        new SettingsNavigationItem(
LauncherAppearance.Capture(),            Resources.SettingsLogs,
            "logs",
            Category,
            () => _select("logs"),
            LauncherButtons.Navigation(Category == "logs")),
        new SettingsNavigationItem(
LauncherAppearance.Capture(),            Resources.SettingsAssistant,
            "ai",
            Category,
            () => _select("ai"),
            LauncherButtons.Navigation(Category == "ai")),
        new SettingsNavigationItem(
LauncherAppearance.Capture(),            Resources.SettingsAdvancedNavigation,
            "advanced",
            Category,
            () => _select("advanced"),
            LauncherButtons.Navigation(Category == "advanced")),
        new SettingsNavigationItem(
            LauncherAppearance.Capture(),
            Resources.SettingsAbout,
            "about",
            Category,
            () => _select("about"),
            LauncherButtons.Navigation(Category == "about")),
    ])
        .Spacing(5f));
}

// Resolve theme-dependent values in the retained navigation control, where reactive theme reads are tracked.

// A pure view with unchanged title/category inputs must receive the new palette as an explicit value.

[View]
internal readonly partial record struct SettingsNavigationItem(
    LauncherAppearance Theme,
    string Title,
    string Id,
    string Current,
    Action Select,
    ButtonStyle Appearance)
{
    /// <summary>Builds one localized category action using the current navigation palette.</summary>
    public Ui Body => LauncherControls.Command(
Theme, Grid(
            [GridLength.Star()],
            [GridLength.Auto],
            [
                Text(Title)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                    .Cell(0, 0),
            ]),
        Select)
        .Style(Appearance.Padding(Id == Current ? 10f : 11f, 0f)
            .ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center))
        .AsSelected(Id == Current)
        .MinHeight(40f)
        .Automation(new()
        {
            Name = Title,
            AutomationId = "settings.category." + Id
        });
}