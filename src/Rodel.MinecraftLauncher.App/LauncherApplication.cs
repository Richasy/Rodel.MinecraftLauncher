// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.SeedExplorer;
using Rodel.MinecraftLauncher.App.Features.Settings;
using Rodel.MinecraftLauncher.App.Features.Shell;
using Rodel.MinecraftLauncher.App.Features.Skins;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using Sprout;
using Sprout.Graphics;
using Sprout.WindowsAppSDK;

namespace Rodel.MinecraftLauncher.App;

/// <summary>Owns application startup, shared service registration and the main window's lifecycle.</summary>
internal sealed class LauncherApplication : Application
{
    private LauncherShell? _shell;
    private SkinEditorWindow? _skinEditor;
    private SeedExplorerWindow? _seedExplorer;

    internal static void ConfigureServices(ServiceRegistry services, AppImageCache imageCache)
    {
        services.AddSingleton(imageCache);
        services.AddSingleton<AppSettings>(static _ => AppSettings.Open());
        services.AddSingleton<LauncherClient>(static provider => CreateClient(provider.GetRequiredService<AppSettings>()));
    }

    /// <inheritdoc/>
    protected override void OnLaunched(LaunchEventArgs e)
    {
        LauncherTheme.Configure(Theme);
        var settings = e.Services.GetRequiredService<AppSettings>();
        LanguageSection.Apply(settings.Language);
        var client = e.Services.GetRequiredService<LauncherClient>();
        var imageCache = e.Services.GetRequiredService<AppImageCache>();
        Theme.SetRequestedMode(settings.ThemePreference);
        var window = OpenWindow(new WindowSpec
        {
            Title = Resources.AppTitle,
            Size = new Size(1280f, 800f),
            Placement = WindowPreferences.Read(settings),
            MinSize = new Size(800f, 560f),
            ExtendsContentIntoTitleBar = true,
            TitleBarHeight = new TitleBarHeight(36f),
            CaptionButtons = CaptionButton.All,
            Colors = new TitleBarColors(Active: new ChromeColors(Background: Colors.Transparent), Inactive: new ChromeColors(Background: Colors.Transparent)),
        });
        window.EnableDragDrop();
        LanguageSection.Apply(settings.Language, window);
        var shell = new LauncherShell(
            client,
            window,
            settings,
            this,
            imageCache,
            () => ShowSkinEditor(window),
            () => ShowSeedExplorer(window),
            selection => ShowSeedExplorer(window, selection));
        _shell = shell;
        window.SetContent(shell);
        if (PackageIdentity.IsPackaged)
            _ = window.UsePackageIcon();
        WindowPreferences.Attach(window, settings);
        window.Closed += OnWindowClosed;
        window.SetTitleBarContent(new Features.Updates.UpdateTitleBar(window, new(settings), shell.InstallUpdateAsync));
        window.Show();
    }

    /// <inheritdoc/>
    protected override void OnActivated(AppActivationArguments args) => _shell?.RestoreWindow();

    private void ShowSkinEditor(Window window)
    {
        _skinEditor ??= new(this, window, Services.GetRequiredService<LauncherClient>());
        _skinEditor.Show();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _shell = null;
        _skinEditor = null;
        _seedExplorer = null;
    }

    private void ShowSeedExplorer(Window window, Core.Worlds.Maps.WorldMapSelection? selection = null)
    {
        _seedExplorer ??= new(this, window, Services.GetRequiredService<LauncherClient>());
        _seedExplorer.Show(selection);
    }

    private static LauncherClient CreateClient(AppSettings settings)
    {
        var configuredRoot = string.IsNullOrWhiteSpace(settings.DataDirectory) ? null : settings.DataDirectory;
        return new LauncherClient(new LauncherPaths(configuredRoot, cacheDirectory: configuredRoot is null ? Path.Combine(AppStorage.CachePath, "Launcher") : null, applicationDataDirectory: AppStorage.DataDirectory));
    }
}