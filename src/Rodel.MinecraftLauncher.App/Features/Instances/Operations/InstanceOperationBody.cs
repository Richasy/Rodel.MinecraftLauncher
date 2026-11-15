// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Semantics;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

[View]
internal readonly partial record struct InstanceOperationBody(
    LauncherAppearance Theme,
    Window Window,
    InstanceManagementState State,
    string Title,
    string Explanation,
    string[] BackupNames,
    string Status,
    string Progress,
    double? Percent,
    InstanceManagementController Controller)
{
    public Ui Body => Column([
        new InstanceOperationFields(
Theme,            Window,
            Explanation,
            BackupNames,
            State,
            Controller),
        Column([
            Text(Status)
                .FontSize(12f).Foreground(State.Error.Length > 0 || State.Conflict || State.StatusCode is "failed" or "interrupted" ? Theme.Error : Theme.Ink)
                .Wrap()
                .Automation(new()
                {
                    Name = Status,
                    AutomationId = "instances.operation.status",
                    LiveSetting = AutomationLiveSetting.Polite
                }),
            Text(State.FailureReport)
                .FontSize(12f).Foreground(Theme.Error)
                .Wrap()
                .Visible(!State.Active && State.FailureReport.Length > 0)
                .Automation(new()
                {
                    Name = State.FailureReport,
                    AutomationId = "instances.operation.error"
                }),
            Text(Progress)
                .FontSize(11f).Foreground(Theme.Muted)
                .Visible(State.Active && Progress.Length > 0),
            ProgressBar()
                .Indeterminate(Percent is null)
                .Value(Percent ?? 0)
                .Thickness(3f)
                .Foreground(Theme.Focus)
                .Track(Theme.Edge)
                .Visible(State.Active)
                .Automation(new()
                {
                    Name = Title,
                    AutomationId = "instances.operation.progress"
                }),
        ])
            .Spacing(8f)
            .Padding(new Thickness(24f, 0f, 24f, 20f))
            .Visible(State.Action != InstanceAction.Rename && Status.Length > 0 && (State.Active || State.Conflict || State.Error.Length > 0 || State.StatusCode is "failed" or "interrupted")),
    ]);
}