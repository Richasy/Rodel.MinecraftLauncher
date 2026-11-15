// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Import;

internal enum ImportPathKind
{
    Source,
    Shared,
    Game,
    LauncherConfig
}

internal sealed partial class ImportFields : Control
{
    private readonly ImportController _controller;
    private readonly Action<ImportPathKind> _browse;
    private readonly Dictionary<ImportPathKind, FileDropHandler> _drops = [];
    private partial ImportPathKind? Dropping { get; set; }

    internal ImportFields(ImportController controller, Action<ImportPathKind> browse)
    {
        _controller = controller;
        _browse = browse;
        State = controller.State;
    }

    internal partial ImportState State { get; private set; }
    /// <summary>Gets whether the optional source path inputs are expanded.</summary>
    public partial bool MoreOptions { get; private set; }
    /// <summary>Gets or sets whether a native picker temporarily owns path input.</summary>
    public partial bool InputBlocked { get; set; }
    private bool Editable => !State.Active && !InputBlocked && State.RegisteredId is null;

    private FileDropHandler DropFor(ImportPathKind kind, Action<string> change)
    {
        if (!_drops.TryGetValue(kind, out var handler))
        {
            handler = new(paths => Editable && paths.Count == 1 && (kind == ImportPathKind.LauncherConfig ? File.Exists(paths[0]) : Directory.Exists(paths[0])),
                paths => change(paths[0]), active => Dropping = active ? kind : Dropping == kind ? null : Dropping);
            _drops.Add(kind, handler);
        }

        return handler;
    }

    private Ui Body => Column([
        Text(Resources.ImportAssociation)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap(),
        new PathSummaryView(Resources.ImportDestination, State.DestinationDirectory, "import.destination", Appearance: PathSummaryAppearance.Capture()),
        Text(Resources.ImportDirectory)
            .FontSize(12f).Foreground(LauncherTheme.Ink),
        new ImportPathInput(
LauncherAppearance.Capture(),            Resources.ImportDirectory,
            State.Directory,
            Editable,
            _controller.ChangeDirectory,
            () => _browse(ImportPathKind.Source),
            "import.directory",
            "import.browse",
            string.Format(Resources.BrowsePath, Resources.ImportDirectory),
            LauncherStyles.Input,
            LauncherButtons.BrowseButton,
            DropHandler: DropFor(ImportPathKind.Source, _controller.ChangeDirectory),
            Dragging: Dropping == ImportPathKind.Source),
        Border(Column([
                Button(
                    Grid(
                        [GridLength.Star(), GridLength.Auto],
                        [GridLength.Auto],
                        [
                            Text(Resources.ImportDirectoryOptions)
                                .FontSize(12f)
                                .Cell(0, 0),
                            LauncherIcons.Line(MoreOptions ? "chevron-up" : "chevron-down", 14f)
                                .Cell(1, 0),
                        ]),
                    () => MoreOptions = !MoreOptions)
                    .Style(LauncherButtons.Disclosure.BorderThickness(0f))
                    .HAlign(HorizontalAlignment.Stretch)
                    .Automation(new()
                    {
                        Name = MoreOptions ? Resources.ImportCollapseOptions : Resources.ImportExpandOptions,
                        AutomationId = "import.options",
                    }),
                Column([
                    Text(Resources.ImportDirectoryOptionsHelp)
                        .FontSize(11f).Foreground(LauncherTheme.Muted)
                        .Wrap(),
                    Text(Resources.ImportSharedInput)
                        .FontSize(12f).Foreground(LauncherTheme.Muted),
                    new ImportPathInput(
LauncherAppearance.Capture(),                        Resources.ImportSharedInput,
                        State.SharedInput,
                        Editable,
                        _controller.ChangeSharedDirectory,
                        () => _browse(ImportPathKind.Shared),
                        "import.shared-input",
                        "import.shared-input.browse",
                        string.Format(Resources.BrowsePath, Resources.ImportSharedInput),
                        LauncherStyles.Input,
                        LauncherButtons.BrowseButton,
                        DropHandler: DropFor(ImportPathKind.Shared, _controller.ChangeSharedDirectory),
                        Dragging: Dropping == ImportPathKind.Shared),
                    Text(Resources.ImportGameInput)
                        .FontSize(12f).Foreground(LauncherTheme.Muted),
                    new ImportPathInput(
LauncherAppearance.Capture(),                        Resources.ImportGameInput,
                        State.GameInput,
                        Editable,
                        _controller.ChangeGameDirectory,
                        () => _browse(ImportPathKind.Game),
                        "import.game-input",
                        "import.game-input.browse",
                        string.Format(Resources.BrowsePath, Resources.ImportGameInput),
                        LauncherStyles.Input,
                        LauncherButtons.BrowseButton,
                        DropHandler: DropFor(ImportPathKind.Game, _controller.ChangeGameDirectory),
                        Dragging: Dropping == ImportPathKind.Game),
                    Text(Resources.ImportLauncherConfig)
                        .FontSize(12f).Foreground(LauncherTheme.Muted),
                    new ImportPathInput(
LauncherAppearance.Capture(),                        Resources.ImportLauncherConfig,
                        State.LauncherConfig,
                        Editable,
                        _controller.ChangeLauncherConfig,
                        () => _browse(ImportPathKind.LauncherConfig),
                        "import.launcher-config",
                        "import.launcher-config.browse",
                        string.Format(Resources.BrowsePath, Resources.ImportLauncherConfig),
                        LauncherStyles.Input,
                        LauncherButtons.BrowseButton,
                        IsFile: true,
                        DropHandler: DropFor(ImportPathKind.LauncherConfig, _controller.ChangeLauncherConfig),
                        Dragging: Dropping == ImportPathKind.LauncherConfig),
                ])
                    .Spacing(8f)
                    .Padding(12f)
                    .Visible(MoreOptions),
            ])
                .Spacing(0f))
            .BorderBrush(LauncherTheme.Edge)
            .BorderThickness(1f),
        Text(Resources.ImportPreviewAgain)
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(State.PreviewDirty),
        Column([
            Text(State.SourceName)
                .FontSize(12f).FontWeight(600).Foreground(LauncherTheme.Ink),
            new PathSummaryView(State.SharedDirectory ? Resources.ImportCombinedPathLabel : Resources.ImportGamePathLabel, State.GameDirectory, "import.game-directory", Appearance: PathSummaryAppearance.Capture()),
            new PathSummaryView(Resources.ImportRuntimePathLabel, State.MinecraftDirectory, "import.runtime-directory", Appearance: PathSummaryAppearance.Capture()).Visible(!State.SharedDirectory),
            Text(Resources.ImportChooseGameDirectory)
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Visible(State.NeedsGameDirectory),
            ItemsRepeater(State.GameDirectoryCandidates, d => d, d => new ImportDirectoryChoice(
LauncherAppearance.Capture(),                    d == State.MinecraftDirectory ? Resources.ImportSharedChoice : Resources.ImportVersionChoice,
                    d,
                    "import.game-choice." + (d == State.MinecraftDirectory ? "shared" : "version"),
                    Editable,
                    () => _ = _controller.SelectGameDirectoryAsync(d)))
                .KeyboardNavigation()
                .Visible(State.NeedsGameDirectory),
            Text(Resources.ImportProfile)
                .FontSize(12f).Foreground(LauncherTheme.Ink)
                .Visible(State.Profiles.Count > 1),
            Text(Resources.ImportChooseProfile)
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Visible(State.NeedsProfile),
            ItemsRepeater(State.Profiles, p => p.Id, p => new ImportChoice(
LauncherAppearance.Capture(),                    string.Format(Resources.ImportProfileChoice, p.Name, p.Version.Length > 0 ? p.Version : Resources.ValueNotAvailable),
                    "import.profile." + p.Id,
                    p.Id == State.Profile,
                    Editable,
                    () => _ = _controller.PreviewAsync(p.Id)))
                .KeyboardNavigation()
                .Visible(State.Profiles.Count > 1),
            Text(Resources.ImportName)
                .FontSize(13f).FontWeight(600).Foreground(LauncherTheme.Ink),
            TextBox(State.Name, _controller.ChangeName)
                .CornerRadius(0f)
                .Enabled(Editable)
                .Automation(new()
                {
                    Name = Resources.ImportName,
                    AutomationId = "import.name"
                }),
            Text(Resources.LaunchVersion)
                .FontSize(13f).FontWeight(600).Foreground(LauncherTheme.Ink),
            TextBox(State.Version, _controller.ChangeVersion)
                .CornerRadius(0f)
                .Enabled(Editable && !State.NeedsProfile)
                .Automation(new()
                {
                    Name = Resources.LaunchVersion,
                    AutomationId = "import.version",
                }),
            Text(State.NeedsVersion ? Resources.ImportChooseVersion : Resources.ImportUnknownVersion)
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Visible(string.IsNullOrWhiteSpace(State.Version)),
            Text(Resources.ImportLocalVersions)
                .FontSize(12f).Foreground(LauncherTheme.Ink)
                .Visible(State.Versions.Count > 1),
            ItemsRepeater(State.Versions, v => v, v => new ImportChoice(
LauncherAppearance.Capture(),                    v,
                    "import.version." + v,
                    v == State.Version,
                    Editable && !State.NeedsProfile,
                    () => _ = _controller.SelectVersionAsync(v)))
                .KeyboardNavigation()
                .Visible(State.Versions.Count > 1),
            new PathSummaryView(Resources.ImportJavaLabel, State.JavaPath, "import.java-path", Appearance: PathSummaryAppearance.Capture()).Visible(State.JavaPath.Length > 0),
            Text(Resources.ImportDefaultJava)
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Visible(State.JavaPath.Length == 0),
            Text(State.MemoryDescription)
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Wrap(),
            Text(Resources.ImportWarnings)
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Visible(State.Preview?.Warnings is System.Text.Json.Nodes.JsonArray { Count: > 0 }),
        ])
            .Spacing(8f)
            .Visible(State.Preview is not null),
    ])
        .Spacing(10f);

    protected override IDisposable? OnMounted()
    {
        _controller.StateChanged += OnStateChanged;
        State = _controller.State;
        return null;
    }

    private void OnStateChanged(ImportState state) => State = state;

    protected override void OnDispose()
    {
        _controller.StateChanged -= OnStateChanged;
        base.OnDispose();
    }
}