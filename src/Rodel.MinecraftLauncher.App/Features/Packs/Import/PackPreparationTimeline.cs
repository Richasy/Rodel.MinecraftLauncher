// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Packs;

internal sealed record PackPreparationStep(
    string Id,
    string Title,
    string Status,
    string Detail,
    string Progress,
    double? Percent)
{
    internal static PackPreparationStep Read(string id, PackImportState state)
    {
        var step = state.Preparation?.Steps.FirstOrDefault(item => item.Id == id);
        var status = step?.Status ?? (id == "files"
            ? state.Active
            ? "running"
            : state.Outcome == "canceled" ? "canceled" : "pending"
            : "pending");
        var title = id switch
        {
            "files" => Resources.PackStepFiles,
            "runtime" => Resources.PackStepRuntime,
            _ => Resources.PackStepJava
        };
        var detail = status switch
        {
            "completed-with-notices" => string.Join(Environment.NewLine, (step?.Runtime?.Warnings ?? []).Concat([step?.Java?.JavaWarning, step?.Java?.Java?.CompatibilityNote]).Where(item => !string.IsNullOrWhiteSpace(item))),
            "completed" => id switch
            {
                "files" => string.Format(Resources.PackStepFilesDone, step?.Acquisition?.LocalDeclaredFiles.ToString() ?? "0", step?.Acquisition?.DownloadedDeclaredFiles.ToString() ?? "0"),
                "runtime" => string.Format(Resources.PackStepRuntimeDone, Isolate(step?.Runtime?.VersionId ?? string.Empty)),
                _ => string.Format(Resources.PackStepJavaDone, Isolate(step?.Java?.Java?.SelectedVersion ?? step?.Java?.Java?.SelectedMajor?.ToString() ?? string.Empty))
            },
            "running" => state.Status,
            "failed" or "blocked" => step?.Error ?? state.Error,
            "canceled" => Resources.FeatureTaskCanceled,
            "skipped" => Resources.PackStepSkipped,
            _ => Resources.PackStepPending
        };
        var progress = state.TaskProgress;
        var percent = progress?.OverallPercent;
        var hasCurrentProgress = progress?.Path is not null || progress?.ProcessorCount is not null;
        return new(
            id,
            title,
            status,
            detail,
            status == "running" && hasCurrentProgress ? state.Progress : string.Empty,
            percent);
    }

    private static string Isolate(string value) => "\u2066" + value + "\u2069";
}

[View]
internal readonly partial record struct PackPreparationTimeline(LauncherAppearance Theme, PackImportState State)
{
    internal PackPreparationStep Files => PackPreparationStep.Read("files", State);
    internal PackPreparationStep Runtime => PackPreparationStep.Read("runtime", State);
    internal PackPreparationStep Java => PackPreparationStep.Read("java", State);
    internal string Count => string.Format(Resources.PackWorkflowProgress, (State.Preparation?.Steps ?? []).Count(step => step.Status is "completed" or "completed-with-notices" or "skipped"), 3);

    /// <summary>Keeps every preparation step visible while completed results accumulate above the current loading step.</summary>
    public Ui Body => Column([
        new PackPreparationStepView(Theme,Files),
        new PackPreparationStepView(Theme,Runtime),
        new PackPreparationStepView(Theme,Java),
        Text(Count)
            .FontSize(12f).Foreground(Theme.Muted)
            .Automation(new()
            {
                Name = Count,
                AutomationId = "pack.workflow.progress",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
    ])
        .Spacing(12f)
        .Padding(0f, 24f);
}

[View]
internal readonly partial record struct PackPreparationStepView(LauncherAppearance Theme, PackPreparationStep Step)
{
    /// <summary>Updates one fixed step in place without navigating to a success, error or progress page.</summary>
    public Ui Body => Overlay([
        new LauncherNotice(Theme,Step.Title, Step.Detail, LauncherNoticeKind.Success, "pack.step." + Step.Id + ".completed").Visible(Step.Status == "completed"),
        new LauncherNotice(Theme,Step.Title, Step.Detail, LauncherNoticeKind.Information, "pack.step." + Step.Id + ".notices").Visible(Step.Status == "completed-with-notices"),
        new LauncherNotice(Theme,Step.Title, Step.Detail, Step.Status == "canceled" ? LauncherNoticeKind.Information : LauncherNoticeKind.Error, "pack.step." + Step.Id + ".problem").Visible(Step.Status is "failed" or "blocked" or "canceled"),
        Border(Grid(
                [GridLength.Pixels(24f), GridLength.Star()],
                [GridLength.Auto],
                [
                    LauncherIcons.Line(Step.Status == "running" ? "refresh" : "info", 24f, Theme.Muted)
                        .Cell(0, 0),
                    Column([
                        Text(Step.Title)
                            .FontSize(14f).FontWeight(600).Foreground(Theme.Ink)
                            .Wrap()
                            .Automation(new()
                            {
                                Name = Step.Title,
                                AutomationId = "pack.step." + Step.Id + ".pending"
                            }),
                        Text(Step.Detail)
                            .FontSize(13f).Foreground(Theme.Muted)
                            .Wrap(),
                        ProgressBar()
                            .Indeterminate(Step.Percent is null)
                            .Value(Step.Percent ?? 0d)
                            .Thickness(3f)
                            .Visible(Step.Status == "running")
                            .Automation(new()
                            {
                                Name = Step.Title,
                                AutomationId = "pack.step." + Step.Id + ".loading"
                            }),
                        Flow(FlowDirection.LeftToRight, Text(Step.Progress)
                                .FontSize(12f).Foreground(Theme.Muted)
                                .Wrap())
                            .Visible(Step.Status == "running" && Step.Progress.Length > 0),
                    ])
                        .Spacing(8f)
                        .Cell(1, 0),
                ])
                .ColumnSpacing(12f))
            .Padding(16f)
            .Background(Theme.ControlBackground)
            .BorderBrush(Theme.Edge)
            .BorderThickness(1f)
            .Visible(Step.Status is "pending" or "running" or "skipped"),
    ]);
}