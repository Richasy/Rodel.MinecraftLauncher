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

internal sealed partial class GeneralSettingsSection(SettingsPage owner) : Control
{
    private readonly LauncherSettingsCard _language = new(Resources.LanguageHeading, owner.Language, owner.Window, () => Resources.LanguageHeading);
    private readonly LauncherSettingsCard _data = new(Resources.DataDirectoryHeading, owner.DataDirectory, owner.Window, () => Resources.DataDirectoryHeading);
    private readonly LauncherChoiceSetting _theme = new(owner, "theme");
    private readonly LauncherChoiceSetting _afterLaunch = new(owner, "after-launch");
    private readonly LauncherChoiceSetting _closeBehavior = new(owner, "close-behavior");
    private readonly MultiplayerSettingsGroup _multiplayer = new(owner.AppPreferences, owner.Window, owner.Multiplayer);

    internal void ExpandMultiplayer() => _multiplayer.Expand();

    private Ui Body => Column([_language, _theme, owner.Wallpapers, _multiplayer, _afterLaunch, _closeBehavior, _data,])
        .Spacing(10f);
}