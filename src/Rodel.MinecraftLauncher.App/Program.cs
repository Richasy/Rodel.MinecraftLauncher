// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Backend.D2D;
using Sprout.HttpImages;
using Sprout.Input;
using Sprout.Svg;
using Sprout.Theming;

using var installation = InstallationLifetime.Open();
var imageCache = new AppImageCache(Path.Combine(AppStorage.CachePath, "Images"));
return SproutApp.CreateBuilder(args)
    .UseApp(static () => new LauncherApplication())
    .ConfigureServices(services => LauncherApplication.ConfigureServices(services, imageCache))
    .UseRenderBackend(new D2DBackend
    {
        MaximumCachedTextureMemory = 64UL * 1024 * 1024,
    })
    .UseSvg()
    .UseHttpImages(imageCache.Loader)
    .UseTheme(ThemeRequest.FollowSystem)
    .ConfigureStyles(static styles =>
    {
        // Edge-filling rows must retain their whole keyboard ring inside the scroll viewport.
        FocusVisualStyle.Default = new(Margin: 0f);
        styles.Add(LauncherStyles.Text);
        // Application defaults are immutable; keep live product colors in tracked controls and views.
        styles.Add(LauncherButtons.Geometry);
        styles.Add(LauncherStyles.Input);
        styles.Add(LauncherStyles.Progress);
        styles.Add(LauncherMenus.Flyout);
        styles.Add(LauncherMenus.ToolTip);
    })
    .Build()
    .Run();