// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Globalization;
using Windows.ApplicationModel;
using Windows.Storage;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

/// <summary>Owns the read-only application information and delegates explicit links to Windows.</summary>
internal sealed partial class AboutSettingsSection : Control
{
    internal const string Repository = "https://github.com/Richasy/Rodel.MinecraftLauncher";
    internal const string Issues = Repository + "/issues";
    internal const string Guide = "https://rmcl.richasy.net/docs/start/install/";
    internal const string Publisher = "云之幻";
    internal static readonly string DeveloperStore = "ms-windows-store://publisher/?name=" + Uri.EscapeDataString(Publisher);
    private readonly Window _window;
    private readonly string _version;
    private readonly Func<AboutCreditPair, AboutCreditRow> _creditFactory;
    private bool _disposed;

    internal AboutSettingsSection(Window window)
    {
        _window = window;
        if (PackageIdentity.IsPackaged)
        {
            var version = Package.Current.Id.Version;
            _version = string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}");
        }
        else
        {
            _version = typeof(AboutSettingsSection).Assembly.GetName().Version!.ToString(4);
        }

        _creditFactory = entry => new AboutCreditRow(entry, this, Open);
        Compact = window.ClientSize.Width < 1100f;
    }

    internal partial bool Compact { get; private set; }
    private ImageSource AppIcon => _window.Icon?.AsImage ?? ImageSource.FromFile(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "launcher.png"));
    private GridLength[] LinkColumns => Compact ? [GridLength.Star()] : [GridLength.Star(), GridLength.Star(), GridLength.Star()];
    private GridLength[] LinkRows => Compact ? [GridLength.Auto, GridLength.Auto, GridLength.Auto] : [GridLength.Auto];

    private Ui Body => Column([
        new AboutHeaderView(LauncherAppearance.Capture(), AppIcon, _version, Compact, () => CopyVersion(), target => Open(target)),
        Grid(
            LinkColumns,
            LinkRows,
            [
                new AboutQuickLinkView(LauncherAppearance.Capture(), "code", Resources.AboutRepository, Resources.AboutRepositoryDescription, Repository, "repository", target => Open(target))
                    .Cell(0, 0),
                new AboutQuickLinkView(LauncherAppearance.Capture(), "book", Resources.AboutGuide, Resources.AboutGuideDescription, Guide, "guide", target => Open(target))
                    .Cell(Compact ? 0 : 1, Compact ? 1 : 0),
                new AboutQuickLinkView(LauncherAppearance.Capture(), "chat", Resources.AboutFeedback, Resources.AboutFeedbackDescription, Issues, "feedback", target => Open(target))
                    .Cell(Compact ? 0 : 2, Compact ? 2 : 0),
            ])
            .ColumnSpacing(8f)
            .RowSpacing(8f),
        Column([
            Text(Resources.AboutCredits)
                .FontSize(19f).FontWeight(600).Foreground(LauncherTheme.Ink)
                .Automation(new() { Name = Resources.AboutCredits, AutomationId = "settings.about.credits" }),
            Text(Resources.AboutCreditsDescription)
                .FontSize(12f).Foreground(LauncherTheme.Muted).Wrap(),
            ItemsRepeater(
                AboutCredits.Rows,
                static entry => entry.First.Id,
                _creditFactory,
                static (row, entry) => row.Entry = entry,
                new StackLayout
                {
                    EstimatedItemExtent = Compact ? 80f : 40f,
                })
                .Margin(new Thickness(0f, 8f, 0f, 0f)),
            Flow(FlowDirection.LeftToRight,
                Text("Terracotta © Burning_TNT and contributors")
                    .FontSize(12f).Foreground(LauncherTheme.Muted).Wrap()),
            HyperlinkButton(Resources.AboutIntegrationException, "https://github.com/burningtnt/Terracotta/tree/v0.4.2#许可")
                .OnNavigate(Open)
                .Style(LauncherAppearance.Capture().Buttons.Hyperlink(true))
                .Automation(new()
                {
                    Name = Resources.AboutIntegrationException,
                    AutomationId = "settings.about.terracotta-exception"
                }),
        ]).Spacing(4f),
        Border(Column([
            Row([
                Flow(FlowDirection.LeftToRight, Text("© Richasy · RMCL").FontSize(12f).Foreground(LauncherTheme.Muted))
                    .VAlign(VerticalAlignment.Center),
                HyperlinkButton(Resources.AboutPrivacy, "https://rmcl.richasy.net/docs/project/privacy/")
                    .OnNavigate(Open)
                    .Style(LauncherAppearance.Capture().Buttons.Hyperlink(true))
                    .Automation(new() { Name = Resources.AboutPrivacy, AutomationId = "settings.about.privacy" })
                    .VAlign(VerticalAlignment.Center),
                HyperlinkButton(Resources.AboutTerms, "https://rmcl.richasy.net/docs/project/terms/")
                    .OnNavigate(Open)
                    .Style(LauncherAppearance.Capture().Buttons.Hyperlink(true))
                    .Automation(new() { Name = Resources.AboutTerms, AutomationId = "settings.about.terms" })
                    .VAlign(VerticalAlignment.Center),
            ]).Spacing(16f).VAlign(VerticalAlignment.Center),
            Text(Resources.AboutTrademark)
                .FontSize(12f).Foreground(LauncherTheme.Muted).Wrap(),
        ]).Spacing(10f))
            .BorderBrush(LauncherTheme.Edge)
            .BorderThickness(0f, 1f, 0f, 0f)
            .Padding(0f, 16f, 0f, 0f),
    ])
        .Spacing(24f);

    private void CopyVersion()
    {
        var text = string.Concat("RMCL ", _version, Environment.NewLine, Repository);
        try
        {
            if (Sprout.Host.Clipboard.SetText(text, _window.Handle))
                LauncherToast.Show(_window, Resources.AboutVersionCopied);
            else
                LauncherToast.ShowTransientError(_window, Resources.LaunchErrorCopyFailed);
        }
        catch (Exception)
        {
            LauncherToast.ShowTransientError(_window, Resources.LaunchErrorCopyFailed);
        }
    }

    private void Open(string target) => _ = OpenAsync(target);

    private async Task OpenAsync(string target)
    {
        try
        {
            var uri = new Uri(target);
            var opened = uri.Scheme == "ms-appx"
                ? await Windows.System.Launcher.LaunchFileAsync(await StorageFile.GetFileFromApplicationUriAsync(uri))
                : await Windows.System.Launcher.LaunchUriAsync(uri);
            if (!opened && !_disposed)
                LauncherToast.ShowTransientError(_window, string.Format(Resources.AboutOpenFailed, target));
        }
        catch (Exception exception)
        {
            if (!_disposed)
                LauncherToast.ShowTransientError(_window, string.Format(Resources.AboutOpenError, target, exception.Message));
        }
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs args) => Compact = args.Size.Width < 1100f;

    protected override IDisposable? OnMounted()
    {
        Compact = _window.ClientSize.Width < 1100f;
        _window.SizeChanged += OnSizeChanged;
        return null;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        _window.SizeChanged -= OnSizeChanged;
        base.OnDispose();
    }
}

[View]
internal readonly partial record struct AboutHeaderView(
    LauncherAppearance Theme,
    ImageSource Icon,
    string Version,
    bool Compact,
    Action Copy,
    Action<string> Open)
{
    internal GridLength[] Columns => Compact ? [GridLength.Auto, GridLength.Star()] : [GridLength.Auto, GridLength.Star(), GridLength.Auto];
    internal GridLength[] Rows => Compact ? [GridLength.Auto, GridLength.Auto] : [GridLength.Auto];
    internal void OpenDeveloperApps() => Open(AboutSettingsSection.DeveloperStore);

    /// <summary>Shows the installed package version and one explicit publisher-catalog action without update-status claims.</summary>
    public Ui Body => Grid(
        Columns,
        Rows,
        [
            Image(Icon)
                .Width(88f).Height(88f)
                .VAlign(VerticalAlignment.Center)
                .Cell(0, 0),
            Column([
                Text(Resources.AppTitle).FontSize(36f).FontWeight(600).Foreground(Theme.Ink),
                Text(Resources.AboutDescription).FontSize(14f).Foreground(Theme.Muted).Wrap(),
                Row([
                    Flow(FlowDirection.LeftToRight,
                        Text(Version).FontSize(12f).Foreground(Theme.Muted)
                            .Automation(new() { Name = string.Format(Resources.AboutVersionFormat, Version), AutomationId = "settings.about.version" }))
                        .VAlign(VerticalAlignment.Center),
                    Button(Row([LauncherIcons.Line("copy", 15f)]), Copy)
                        .Style(Theme.Buttons.IconButton)
                        .Width(28f).Height(28f)
                        .VAlign(VerticalAlignment.Center)
                        .ToolTip(Resources.AboutCopyVersion)
                        .Automation(new() { Name = Resources.AboutCopyVersion, AutomationId = "settings.about.copy-version" }),
                    Flow(FlowDirection.LeftToRight,
                        HyperlinkButton("Richasy", "https://github.com/Richasy")
                            .OnNavigate(Open)
                            .Style(Theme.Buttons.Hyperlink(true))
                            .Automation(new() { Name = "Richasy", AutomationId = "settings.about.author" }))
                        .VAlign(VerticalAlignment.Center),
                    Text(Resources.AboutFree).FontSize(12f).Foreground(Theme.Muted).VAlign(VerticalAlignment.Center),
                ]).Spacing(8f).VAlign(VerticalAlignment.Center),
            ])
                .Spacing(6f)
                .Cell(1, 0),
            Column([
                LauncherControls.Command(Theme,
                    Row([
                        LauncherIcons.Line("store", 18f),
                        Text(Resources.AboutDeveloperApps),
                        LauncherIcons.Line("external-link", 14f),
                    ]).Spacing(8f),
                    OpenDeveloperApps)
                    .Style(Theme.Buttons.Accent)
                    .AsPrimary()
                    .Automation(new()
                    {
                        Name = Resources.AboutDeveloperApps,
                        HelpText = AboutSettingsSection.DeveloperStore,
                        AutomationId = "settings.about.developer-apps"
                    }),
                Flow(FlowDirection.LeftToRight,
                    Text(string.Concat(AboutSettingsSection.Publisher, " · Microsoft Store"))
                        .FontSize(12f).Foreground(Theme.Muted)),
            ])
                .Spacing(8f)
                .VAlign(VerticalAlignment.Center)
                .Cell(Compact ? 1 : 2, Compact ? 1 : 0),
        ])
        .ColumnSpacing(24f)
        .RowSpacing(16f);
}

[View]
internal readonly partial record struct AboutQuickLinkView(
    LauncherAppearance Theme,
    string Icon,
    string Title,
    string Description,
    string Target,
    string Id,
    Action<string> Open)
{
    internal void Activate() => Open(Target);
    internal Sprout.Styling.TextStyle DescriptionStyle => Theme.HighContrast
        ? LauncherStyles.Text.FontSize(12f)
        : LauncherStyles.Text.FontSize(12f).Foreground(Theme.Muted);

    /// <summary>Opens one direct help or project destination without introducing a settings subpage.</summary>
    public Ui Body => Button(
        Grid(
            [GridLength.Auto, GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                LauncherIcons.Line(Icon, 20f).VAlign(VerticalAlignment.Center).Cell(0, 0),
                Column([
                    Text(Title).FontSize(14f).FontWeight(600).Wrap(),
                    Text(Description).Style(DescriptionStyle).Wrap(),
                ]).Spacing(4f).Cell(1, 0),
                LauncherIcons.Line("external-link", 14f).VAlign(VerticalAlignment.Center).Cell(2, 0),
            ])
            .ColumnSpacing(12f),
        Activate)
        .Style(Theme.Buttons.OutlineButton.Padding(16f).ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center))
        .Automation(new()
        {
            Name = Title,
            HelpText = Target,
            AutomationId = "settings.about." + Id
        });
}