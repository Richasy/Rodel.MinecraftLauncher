// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Create;

[View]
internal readonly partial record struct CreateDialogBody(
    LauncherAppearance Theme,
    CreateFields Fields,
    CreateState State,
    string Workflow,
    string Status,
    string FileProgress,
    string CurrentFileProgress,
    string Warning)
{
    public Ui Body => Column([
        Fields,
        Column([
            Text(Workflow)
                .FontSize(12f).Foreground(Theme.Ink)
                .Visible(State.Active)
                .Automation(new()
                {
                    Name = Workflow,
                    AutomationId = "create.workflow"
                }),
            Text(Status)
                .FontSize(12f).Foreground(Theme.Ink)
                .Wrap()
                .Visible(State.Active && Status.Length > 0)
                .Automation(new()
                {
                    Name = Status,
                    AutomationId = "create.status",
                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                }),
            Text(FileProgress)
                .FontSize(11f).Foreground(Theme.Muted)
                .Wrap()
                .Visible(State.Active && FileProgress.Length > 0)
                .Automation(new()
                {
                    Name = FileProgress,
                    AutomationId = "create.file-count"
                }),
            Text(CurrentFileProgress)
                .FontSize(11f).Foreground(Theme.Muted)
                .Wrap()
                .Visible(CurrentFileProgress.Length > 0),
            Text(Warning)
                .FontSize(12f).Foreground(Theme.Warning)
                .Wrap()
                .Visible(Warning.Length > 0)
                .Automation(new()
                {
                    Name = Warning,
                    AutomationId = "create.warning"
                }),
            Text(Resources.CreateError)
                .FontSize(12f).Foreground(Theme.Error)
                .Wrap()
                .Visible(State.Error.Length > 0)
                .Automation(new()
                {
                    Name = Resources.CreateError,
                    AutomationId = "create.error",
                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                }),
        ])
            .Spacing(6f)
            .Visible(State.Active || Warning.Length > 0 || State.Error.Length > 0),
        ProgressBar()
            .Indeterminate(State.Percent is null)
            .Value(State.Percent ?? 0d)
            .Thickness(3f)
            .TrackThickness(3f)
            .Foreground(Theme.Focus)
            .Track(Theme.Edge)
            .Visible(State.Active)
            .Automation(new()
            {
                Name = Resources.CreateOverallFilesTitle,
                AutomationId = "create.total-progress"
            }),
    ])
        .Spacing(12f)
        .Padding(new Thickness(24f, 20f, 24f, 20f));
}