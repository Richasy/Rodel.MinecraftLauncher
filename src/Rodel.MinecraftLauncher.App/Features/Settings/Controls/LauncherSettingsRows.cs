// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Theming;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

/// <summary>Keeps each real preference selector in the shared native setting card.</summary>
internal sealed partial class LauncherChoiceSetting(SettingsPage owner, string kind) : Control
{
    private string Title => kind == "theme"
        ? Resources.ThemeHeading
        : kind == "after-launch" ? Resources.AfterLaunchHeading : Resources.CloseBehaviorHeading;
    private readonly LauncherSettingsCard _card = new(kind == "theme"
            ? Resources.ThemeHeading
            : kind == "after-launch" ? Resources.AfterLaunchHeading : Resources.CloseBehaviorHeading, new LauncherChoiceContent(owner, kind), owner.Window, () => kind == "theme"
            ? Resources.ThemeHeading
            : kind == "after-launch" ? Resources.AfterLaunchHeading : Resources.CloseBehaviorHeading);

    private Ui Body => Border(_card);
}

internal sealed partial class LauncherChoiceContent(SettingsPage owner, string kind) : Control
{
    private string Title => kind == "theme"
        ? Resources.ThemeHeading
        : kind == "after-launch" ? Resources.AfterLaunchHeading : Resources.CloseBehaviorHeading;
    private string[] Items => kind == "theme"
        ? [Resources.ThemeSystem, Resources.ThemeLight, Resources.ThemeDark]
        : kind == "after-launch" ? [Resources.AfterLaunchTray, Resources.AfterLaunchTaskbar, Resources.AfterLaunchKeepVisible] : [Resources.CloseBehaviorExit, Resources.CloseBehaviorTray];
    private int Selected => kind == "theme"
        ? owner.Theme == ThemeRequest.Light
        ? 1
        : owner.Theme == ThemeRequest.Dark ? 2 : 0
        : kind == "after-launch"
        ? owner.AfterLaunch == "Taskbar"
        ? 1
        : owner.AfterLaunch == "KeepVisible" ? 2 : 0
        : owner.CloseBehavior == "Tray" ? 1 : 0;
    private string Error => kind == "theme"
        ? owner.ThemeError
        : kind == "after-launch" ? owner.AfterLaunchError : owner.CloseError;
    private string ErrorText => string.Format(kind == "theme"
            ? Resources.ThemeSaveError
            : kind == "after-launch" ? Resources.AfterLaunchSaveError : Resources.CloseBehaviorSaveError, Error);

    private void Choose(int index)
    {
        if (kind == "theme")
            owner.ChooseTheme(index);
        else if (kind == "after-launch")
            owner.ChooseAfterLaunch(index);
        else
            owner.ChooseCloseBehavior(index);
    }

    private Ui Body => Column([
        new SettingsChoiceRow(
LauncherAppearance.Capture(),            Title,
            Items,
            Selected,
            Choose,
            owner.Width - 188f,
            LauncherTheme.Ink,
            LauncherTheme.Muted,
            LauncherMenus.Choice,
            LauncherMenus.ChoiceFace,
            LauncherMenus.ChoiceMenu,
            "settings." + kind,
            kind == "close-behavior"
                ? Resources.CloseBehaviorHelp
                : kind == "theme" ? Resources.SettingsThemeHelp : Resources.SettingsAfterLaunchHelp),
        Text(ErrorText)
            .FontSize(12f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(Error.Length > 0),
    ])
        .Spacing(8f);
}