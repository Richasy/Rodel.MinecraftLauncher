// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Shell;

[View]
internal readonly partial record struct ShellNavigationView(
    LauncherAppearance Theme,
    string Page,
    Action<string> Navigate,
    Action ShowAccounts,
    string AccountLabel,
    IdentityRecord? Account,
    Features.Accounts.AccountAvatarImages Avatars,
    bool AccountLoading,
    ButtonStyle Command,
    ButtonStyle Selected,
    ButtonStyle Normal,
    Brush Surface,
    Color Edge,
    DropShadow Shadow,
    bool AiAvailable,
    bool MultiplayerAvailable,
    bool CompactHeight,
    float AvailableHeight)
{
    /// <summary>Builds the launcher navigation with shared compact density across every primary destination.</summary>
    public Ui Body => Column([
        LauncherControls.Command(
            Theme,
            Row([
                new Features.Accounts.AccountAvatar(Account, LauncherLayout.AvatarGraphicSize, Avatars, AccountLoading)
            ]),
            ShowAccounts)
            .Style(Command)
            .Automation(new()
            {
                Name = AccountLabel,
                AutomationId = "identity",
            })
            .Width(LauncherLayout.NavigationWidth).Height(LauncherLayout.NavigationWidth)
            .HAlign(HorizontalAlignment.Center),
        Overlay([
            new Controls.LauncherMaterialSurface(Surface).Margin(1f),
            Border(ScrollView(new ShellNavigationItemsView(
                Theme, Page, Navigate, Selected, Normal, Edge, CompactHeight, AiAvailable, MultiplayerAvailable)))
                .Padding(6f)
                .BorderBrush(Edge)
                .BorderThickness(1f)
                .Shadow(Shadow),
        ]).Width(LauncherLayout.NavigationWidth)
            .MaxHeight(Math.Max(0f, AvailableHeight - LauncherLayout.NavigationWidth - 16f)),
    ])
        .Spacing(16f);
}

[View]
internal readonly partial record struct ShellNavigationItemsView(
    LauncherAppearance Theme,
    string Page,
    Action<string> Navigate,
    ButtonStyle Selected,
    ButtonStyle Normal,
    Color Edge,
    bool CompactHeight,
    bool AiAvailable,
    bool MultiplayerAvailable)
{
    internal Action PageAction(string page)
    {
        var navigate = Navigate;
        return () => navigate(page);
    }

    /// <summary>Builds naturally sized navigation items as the scroll viewport's concrete view.</summary>
    public Ui Body => LauncherControls.KeyboardGroup(Column([
        new NavItem(
            Theme,
            Resources.NavHome,
            "home",
            Page == "home",
            PageAction("home"),
            Page == "home" ? Selected : Normal,
            CompactHeight),
        new NavItem(
            Theme,
            Resources.NavInstances,
            "instances",
            Page == "instances",
            PageAction("instances"),
            Page == "instances" ? Selected : Normal,
            CompactHeight),
        new NavItem(
            Theme,
            Resources.NavContent,
            "content",
            Page == "content",
            PageAction("content"),
            Page == "content" ? Selected : Normal,
            CompactHeight),
        new NavItem(
            Theme,
            Resources.MultiplayerHeading,
            "multiplayer",
            Page == "multiplayer",
            PageAction("multiplayer"),
            Page == "multiplayer" ? Selected : Normal,
            CompactHeight).Visible(MultiplayerAvailable),
        new NavItem(
            Theme,
            Resources.NavAssistant,
            "spark",
            Page == "assistant",
            PageAction("assistant"),
            Page == "assistant" ? Selected : Normal,
            CompactHeight).Visible(AiAvailable),
        Rectangle(Edge)
            .Width(24f).Height(1f)
            .HAlign(HorizontalAlignment.Center)
            .Margin(new Thickness(0f, 5f, 0f, 5f)),
        new NavItem(
            Theme,
            Resources.NavSettings,
            "settings",
            Page == "settings",
            PageAction("settings"),
            Page == "settings" ? Selected : Normal,
            CompactHeight),
    ])
        .Spacing(CompactHeight ? 4f : 6f));
}