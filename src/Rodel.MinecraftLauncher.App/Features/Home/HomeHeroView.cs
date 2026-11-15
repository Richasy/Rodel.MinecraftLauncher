// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Wallpapers;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed record HomeServerHero(
    string Address,
    bool HasTarget,
    bool CanLaunch,
    bool CanConfigure,
    bool Preparing);

[View]
internal readonly partial record struct HomeHeroView(
    LauncherAppearance Theme,
    HomePageText Presentation,
    HomePageState State,
    LauncherLayout Layout,
    HomeAppearance Appearance,
    WallpaperService Wallpapers,
    Action Launch,
    Action Configure,
    Action ExpandError,
    Action Cancel,
    HomeServerHero? Server = null,
    bool RightToLeft = false)
{
    internal bool HasTarget => Server?.HasTarget ?? (State.Selected is not null);
    internal string PrimaryLabel => Presentation.LaunchAction;
    internal string PrimaryIcon => HasTarget && State.CanResumeInstallation && !Preparing ? "download" : "play";
    internal string EmptyDescription => Server is null ? Resources.HomeEmptyLocalDescription : Resources.HomeEmptyServerDescription;
    internal string ContextLabel => HasTarget
        ? Server is null ? Resources.CurrentInstanceEdition : Resources.HomeCurrentServer
        : Server is null ? Resources.InstancesLocal : Resources.ServersTab;
    internal bool ReadinessBlocked => HasTarget && Server is null && Presentation.LaunchBlockReason.Length > 0 && !State.CurrentLaunch.Busy;
    internal bool SmallStatus => (State.ShowLaunchStatus || ReadinessBlocked) && Layout.Height < 700f;
    internal bool DiagnosticFallback => Server is not null && State.Selected is not null && !(State.ShowLaunchStatus && State.HasLaunchError);
    internal string ConfigureName => Server is null ? Resources.CurrentInstanceSettings : Resources.HomeServerConfigure;
    internal bool Preparing => (Server?.Preparing ?? (State.Pending || State.CurrentLaunch.TaskActive)) || State.CurrentLaunch.WaitingForWindow;
    internal bool CanCancel => State.CurrentLaunch.TaskActive && State.CurrentLaunch.TaskOwned && !State.Pending && !State.CancelRequested;
    internal bool DiagnosticError => State.HasCriticalLaunchError || (State.CurrentLaunch.Problem is { } problem && LaunchProblemText.OriginalError(problem) is { } error && ((error.Level is "ERROR" or "FATAL") || error.FromFailedProcess));

    public Ui Body => Column([
        new WallpaperText(ContextLabel, 12f, Appearance.SceneQuiet, Wallpapers.Readability).HAlign(HorizontalAlignment.Left),
        new WallpaperText(
            Presentation.Heading,
            Layout.HeadingSize,
            Appearance.SceneInk,
            Wallpapers.Readability,
            true).Automation(new()
            {
                Name = Presentation.Heading,
                AutomationId = "home.heading"
            })
            .HAlign(HorizontalAlignment.Left)
            .Margin(new Thickness(0f, SmallStatus ? 4f : 14f, 0f, SmallStatus ? 4f : 8f)),
        new WallpaperText(EmptyDescription, 16f, Appearance.SceneMuted, Wallpapers.Readability).HAlign(HorizontalAlignment.Left)
            .Margin(new Thickness(0f, 0f, 0f, 24f))
            .Visible(!HasTarget),
        Flow(FlowDirection.LeftToRight, new WallpaperText(Server?.Address ?? string.Empty, 16f, Appearance.SceneMuted, Wallpapers.Readability))
            .HAlign(HorizontalAlignment.Left)
            .Visible(Server is not null && HasTarget),
        new WallpaperText(Presentation.Runtime, 13f, Appearance.SceneMuted, Wallpapers.Readability).HAlign(HorizontalAlignment.Left)
            .Margin(new Thickness(0f, SmallStatus ? 8f : 14f, 0f, SmallStatus ? 10f : 20f))
            .Visible(HasTarget && !ReadinessBlocked && Presentation.Runtime.Length > 0)
            .Automation(new()
            {
                Name = Presentation.Runtime,
                AutomationId = "home.runtime"
            }),
        Row([
            LauncherControls.Command(
Theme,                Row([
                    LauncherIcons.Line(PrimaryIcon, 22f)
                        .VAlign(VerticalAlignment.Center),
                    Text(PrimaryLabel)
                        .LetterSpacing(2f)
                        .FontSize(21f).FontWeight(700)
                        .HAlign(HorizontalAlignment.Stretch).VAlign(VerticalAlignment.Center)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .Weight(1f)
                        .Shrink(1f)
                        .MinContentWidth(0f),
                    Text("↵")
                        .FontSize(18f)
                        .VAlign(VerticalAlignment.Center),
                ])
                    .Spacing(12f),
                Launch)
                .Style(Appearance.Primary)
                .AsPrimary()
                .Enabled(HasTarget && !Preparing && (Server?.CanLaunch ?? State.CanLaunch))
                .Automation(new()
                {
                    Name = PrimaryLabel,
                    HelpText = HasTarget ? Presentation.LaunchBlockReason : EmptyDescription,
                    AutomationId = "launch"
                })
                .MinWidth(Server is null ? 252f : Math.Min(252f, Math.Max(0f, Layout.HeroWidth - ((DiagnosticFallback ? 2f : 1f) * (62f + 12f)))))
                .Shrink(1f)
                .MinContentWidth(0f)
                .MinHeight(62f),
            LauncherControls.Command(
Theme,                Row([
                    LauncherIcons.Line("wrench", 22f)
                ]),
                Configure)
                .Style(Appearance.Command)
                .ToolTip(ConfigureName)
                .Enabled(Server?.CanConfigure ?? State.CanConfigure)
                .Automation(new()
                {
                    Name = ConfigureName,
                    AutomationId = "instance.configure",
                })
                .Width(62f).MinHeight(62f),
            LauncherControls.Command(
Theme,                Row([
                    LauncherIcons.Line(DiagnosticError ? "error" : "info", 22f)
                ]),
                ExpandError)
                .Style(DiagnosticError ? Theme.Buttons.ErrorButton : Appearance.Command)
                .Enabled(!Preparing)
                .ToolTip(Resources.ProblemInspect)
                .Automation(new()
                {
                    Name = Resources.ProblemInspect,
                    HelpText = State.HasLaunchError ? Presentation.LaunchTitle : string.Empty,
                    AutomationId = "home.connection.problem",
                })
                .Width(62f).MinHeight(62f)
                .Visible(DiagnosticFallback),
        ])
            .Spacing(12f)
            .Margin(new Thickness(0f, ReadinessBlocked ? 16f : 0f, 0f, 0f))
            .HAlign(HorizontalAlignment.Left),
        new WallpaperText(Presentation.Configuration, 12f, Appearance.SceneQuiet, Wallpapers.Readability).HAlign(HorizontalAlignment.Left)
            .Margin(new Thickness(0f, 16f, 0f, 0f))
            .Automation(new()
            {
                Name = Presentation.Configuration,
                AutomationId = "home.configuration"
            })
            .Visible(HasTarget && Presentation.Configuration.Length > 0),
        new LaunchStatusView(
Theme,            ReadinessBlocked
                ? State.Details?.LaunchIssue == "version" ? Resources.VersionNotConfigured : Resources.LaunchVersionFilesUnavailable
                : Presentation.LaunchTitle,
            ReadinessBlocked ? Presentation.LaunchBlockReason : Presentation.LaunchDetail,
            ReadinessBlocked ? string.Empty : State.ActionError,
            !ReadinessBlocked && State.HasLaunchError,
            !ReadinessBlocked && State.HasCriticalLaunchError,
            ExpandError,
            Appearance.Surface,
            Appearance.Ink,
            !ReadinessBlocked && State.HasCriticalLaunchError ? Appearance.Error : Appearance.Edge,
            Appearance.Icon,
            State.ShowLaunchProgress,
            State.LaunchPercent,
            Appearance.Progress,
            Appearance.Edge,
            RightToLeft,
            State.CurrentLaunch.Problem is Core.Application.LaunchDiagnosisProblem { ReportedError: not null },
            State.CurrentLaunch.TaskActive ? Cancel : null,
            CanCancel).Margin(new Thickness(0f, 12f, 0f, 0f))
            .Visible(State.ShowLaunchStatus || ReadinessBlocked),
    ]);
}