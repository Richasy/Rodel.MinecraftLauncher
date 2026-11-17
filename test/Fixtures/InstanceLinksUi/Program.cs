// Copyright (c) Richasy. All rights reserved.

extern alias NativeApp;
using NativeApp::Rodel.MinecraftLauncher.App.Features.Settings;
using NativeApp::Rodel.MinecraftLauncher.App.Features.Shell;
using NativeApp::Rodel.MinecraftLauncher.App.Features.Import;
using NativeApp::Rodel.MinecraftLauncher.App.Imaging;
using NativeApp::Rodel.MinecraftLauncher.App.Settings;
using NativeApp::Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using Sprout;
using Sprout.Backend.D2D;
using Sprout.Graphics;
using Sprout.HttpImages;
using Sprout.Svg;
using Sprout.Theming;

return SproutApp.CreateBuilder(args)
    .UseApp(static () => new InstanceLinksApplication())
    .UseRenderBackend(new D2DBackend())
    .UseSvg()
    .UseTheme(ThemeRequest.FollowSystem)
    .UseHttpImages(new AppImageCache(Path.Combine(FixtureRoot.Path, "images")).Loader)
    .ConfigureStyles(static styles =>
    {
        styles.Add(LauncherStyles.Text);
        styles.Add(LauncherButtons.Geometry);
        styles.Add(LauncherStyles.Input);
        styles.Add(LauncherStyles.Progress);
        styles.Add(LauncherMenus.Flyout);
        styles.Add(LauncherMenus.ToolTip);
    }).Build().Run();

/// <summary>Hosts the production shell and instance flows with isolated launcher preferences, without replacing discovery or registration.</summary>
internal sealed class InstanceLinksApplication : Application
{
    /// <inheritdoc/>
    protected override void OnLaunched(LaunchEventArgs e)
    {
        LauncherTheme.Configure(Theme);
        var settings = new AppSettings(new Dictionary<string, object>()) { Language = "zh-CN", CloseBehavior = "Exit" };
        var themeFile = System.IO.Path.Combine(FixtureRoot.Path, "theme");
        if (File.Exists(themeFile))
            Theme.SetRequestedMode(File.ReadAllText(themeFile).Trim() == "light" ? ThemeRequest.Light : ThemeRequest.Dark);
        LanguageSection.Apply(settings.Language);
        var window = OpenWindow(new WindowSpec
        {
            Title = "RMCL",
            Size = new Size(1280f, 800f),
            MinSize = new Size(800f, 560f),
            ExtendsContentIntoTitleBar = true,
            TitleBarHeight = new TitleBarHeight(36f),
            CaptionButtons = CaptionButton.All,
            Colors = new TitleBarColors(Active: new ChromeColors(Background: Colors.Transparent), Inactive: new ChromeColors(Background: Colors.Transparent)),
        });
        LanguageSection.Apply(settings.Language, window);
        var client = new LauncherClient(new LauncherPaths(Path.Combine(FixtureRoot.Path, "launcher")));
        window.SetContent(new LauncherShell(client, window, settings, this, new AppImageCache(Path.Combine(FixtureRoot.Path, "images")),
            static () => { }, static () => { }, static _ => { }));
        window.Show();
        var importSource = System.IO.Path.Combine(FixtureRoot.Path, "import-source");
        if (File.Exists(importSource))
        {
            var controller = new ImportController(client);
            var presenter = new ImportPresenter(window, controller);
            window.Closed += (_, _) => { presenter.Dispose(); controller.Dispose(); };
            presenter.Show();
            controller.ChangeDirectory(File.ReadAllText(importSource).Trim());
        }
    }
}

internal static class FixtureRoot
{
    internal static string Path => System.IO.Path.GetFullPath(System.Environment.GetEnvironmentVariable("RODEL_LINK_FIXTURE_DATA") is { Length: > 0 } directory
        ? directory : @"C:\sprout\data");
}
