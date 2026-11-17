// Copyright (c) Richasy. All rights reserved.

extern alias NativeApp;
using NativeApp::Rodel.MinecraftLauncher.App.Features.Settings;
using NativeApp::Rodel.MinecraftLauncher.App.Features.Shell;
using NativeApp::Rodel.MinecraftLauncher.App.Features.Updates;
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
using System.Net;
using System.Text;
using System.Text.Json;
using ThemeRequest = Sprout.Theming.ThemeRequest;

FixtureFiles.Stage("entry");
return SproutApp.CreateBuilder(args)
    .UseApp(static () => new UpdateFixtureApplication())
    .UseRenderBackend(new D2DBackend())
    .UseSvg()
    .UseTheme(ThemeRequest.FollowSystem)
    .UseHttpImages(new AppImageCache(Path.Combine(FixtureFiles.Root, "images")).Loader)
    .ConfigureStyles(static styles =>
    {
        styles.Add(LauncherStyles.Text);
        styles.Add(LauncherButtons.Geometry);
        styles.Add(LauncherStyles.Input);
        styles.Add(LauncherStyles.Progress);
        styles.Add(LauncherMenus.Flyout);
        styles.Add(LauncherMenus.ToolTip);
    })
    .Build().Run();

/// <summary>Hosts the actual launcher shell and updater with isolated data and a release-only transport substitute.</summary>
internal sealed class UpdateFixtureApplication : Application
{
    /// <inheritdoc/>
    protected override void OnLaunched(LaunchEventArgs e)
    {
        FixtureFiles.Stage("launched");
        LauncherTheme.Configure(Theme);
        var languagePath = Path.Combine(FixtureFiles.Root, "language");
        var settings = new AppSettings(new Dictionary<string, object>())
        {
            Language = File.Exists(languagePath) ? File.ReadAllText(languagePath).Trim() : "zh-CN",
            CloseBehavior = "Tray",
        };
        var skipped = Path.Combine(FixtureFiles.Root, "skipped-version");
        if (File.Exists(skipped))
            settings.SkippedUpdateVersion = File.ReadAllText(skipped);
        settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppSettings.SkippedUpdateVersion))
                File.WriteAllText(skipped, settings.SkippedUpdateVersion);
        };
        LanguageSection.Apply(settings.Language);
        FixtureFiles.Stage("language");
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
        FixtureFiles.Stage("window");
        var client = new LauncherClient(new LauncherPaths(Path.Combine(FixtureFiles.Root, "games"),
            cacheDirectory: Path.Combine(FixtureFiles.Root, "cache"), applicationDataDirectory: Path.Combine(FixtureFiles.Root, "app-data")));
        var shell = new LauncherShell(client, window, settings, this, new AppImageCache(Path.Combine(FixtureFiles.Root, "images")),
            static () => { }, static () => { }, static _ => { });
        FixtureFiles.Stage("shell");
        window.SetContent(shell);
        FixtureFiles.Stage("content");
        var state = new UpdateState(settings, new HttpClient(new ReleaseFixtureHandler()) { Timeout = Timeout.InfiniteTimeSpan },
            Path.Combine(FixtureFiles.Root, "updates"), new Version(0, 2611, 4, 0));
        window.SetTitleBarContent(new UpdateTitleBar(window, state, shell.InstallUpdateAsync));
        FixtureFiles.Stage("titlebar");
        window.Show();
        FixtureFiles.Stage("shown");
    }
}

internal static class FixtureFiles
{
    internal static string Root => @"C:\sprout\data";
    internal static void Stage(string name) => File.AppendAllText(Path.Combine(Root, "startup-stages"), name + Environment.NewLine);
}

/// <summary>Serves real release notes and installer bytes through the production client's unchanged GitHub contract.</summary>
internal sealed class ReleaseFixtureHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = Path.Combine(FixtureFiles.Root, "release-note.json");
        var manifest = JsonSerializer.Deserialize(File.ReadAllText(path), ReleaseJsonContext.Default.ReleaseManifest)!;
        if (request.RequestUri == GitHubUpdateClient.Latest)
        {
            var prefix = GitHubUpdateClient.Repository + "/releases/download/v" + manifest.Version + "/";
            var assets = manifest.Installers.Values.Select(installer => new GitHubAsset
            {
                Name = installer.FileName,
                Size = installer.Size,
                DownloadUrl = prefix + installer.FileName,
            }).Append(new GitHubAsset { Name = "release-note.json", Size = new FileInfo(path).Length, DownloadUrl = prefix + "release-note.json" }).ToArray();
            var release = new GitHubRelease { TagName = "v" + manifest.Version, Assets = assets };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(release, ReleaseJsonContext.Default.GitHubRelease), Encoding.UTF8, "application/json"),
            });
        }

        var name = Uri.UnescapeDataString(request.RequestUri!.Segments[^1]);
        var file = Path.Combine(FixtureFiles.Root, Path.GetFileName(name));
        File.AppendAllText(Path.Combine(FixtureFiles.Root, "requests"), name + Environment.NewLine);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ThrottledFileStream(file)),
        });
    }
}

internal sealed class ThrottledFileStream(string path) : FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
{
    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken);
        return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 65536)], cancellationToken);
    }
}