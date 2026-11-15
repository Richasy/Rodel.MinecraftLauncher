// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Import;

[View]
internal readonly partial record struct ImportDialogBody(
    LauncherAppearance Theme,
    ImportFields Fields,
    ImportState State,
    string Status,
    string ErrorSummary)
{
    public Ui Body => Column([
        Fields,
        ProgressBar()
            .Indeterminate(State.CopyPercent is null)
            .Value(State.CopyPercent ?? 0d)
            .Thickness(3f)
            .TrackThickness(3f)
            .Foreground(Theme.Focus)
            .Track(Theme.Edge)
            .Visible(State.Active)
            .Automation(new()
            {
                Name = Resources.ImportCopying,
                AutomationId = "import.progress"
            }),
        Text(Status)
            .FontSize(12f).Foreground(Theme.Ink)
            .Wrap()
            .Visible(Status.Length > 0 && (State.Active || State.Previewing || (State.RegisteredId is not null && State.Job?.Status != "succeeded")))
            .Automation(new()
            {
                Name = Status,
                AutomationId = "import.status",
                LiveSetting = State.Active && !State.CancelRequested ? Sprout.Semantics.AutomationLiveSetting.Off : Sprout.Semantics.AutomationLiveSetting.Polite
            }),
        Text(ErrorSummary)
            .FontSize(12f).Foreground(Theme.Error)
            .Wrap()
            .Visible(ErrorSummary.Length > 0)
            .Automation(new()
            {
                Name = ErrorSummary,
                AutomationId = "import.error",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
    ])
        .Spacing(12f)
        .Padding(new Thickness(24f, 20f, 24f, 20f));
}