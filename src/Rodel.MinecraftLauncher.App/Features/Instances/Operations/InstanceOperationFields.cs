// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

[View]
internal readonly partial record struct InstanceOperationFields(
    LauncherAppearance Theme,
    Window Window,
    string Explanation,
    string[] BackupNames,
    InstanceManagementState State,
    InstanceManagementController Controller)
{
    internal string ContentReport => State.Result is { } result && State.Action == InstanceAction.Content ? string.Format(
        Resources.InstanceContentReport,
        (result["mods"] as System.Text.Json.Nodes.JsonArray)?.Count ?? 0,
        (result["resourcepacks"] as System.Text.Json.Nodes.JsonArray)?.Count ?? 0,
        (result["shaderpacks"] as System.Text.Json.Nodes.JsonArray)?.Count ?? 0,
        result["worldDirectories"]?.ToString() ?? "0") : string.Empty;

    public Ui Body => Column([
        Text(Explanation)
            .FontSize(State.Action == InstanceAction.Delete ? 14f : 12f).Foreground(State.Action == InstanceAction.Delete ? Theme.Ink : Theme.Muted)
            .Wrap()
            .Visible(Explanation.Length > 0),
        new LauncherCheckBox(
Theme,            Resources.InstanceDeleteFilesChoice,
            State.DeleteFiles,
            Controller.ToggleDeleteFiles,
            !State.Active && !State.Succeeded && State.Instance is { Managed: true },
            "instances.operation.delete-files").Visible(State.Action == InstanceAction.Delete),
        Text(Resources.InstanceFilesNotOwnedIssue)
            .FontSize(12f).Foreground(Theme.Muted)
            .Wrap()
            .Visible(State.Action == InstanceAction.Delete && State.Instance is { Managed: false }),
        Column([
            Text(Resources.ImportName)
                .FontSize(12f).Foreground(Theme.Ink),
            TextBox(State.Name, Controller.ChangeName)
                .Enabled(!State.Active && !State.Succeeded)
                .FontSize(12f)
                .Padding(12f, 10f)
                .MinHeight(40f)
                .Automation(new()
                {
                    Name = Resources.ImportName,
                    AutomationId = "instances.operation.name"
                }),
        ])
            .Spacing(8f)
            .Visible(State.Action is InstanceAction.Rename or InstanceAction.Copy),
        new LaunchSettingsFields(
Theme,            Window,
            (State.Draft ?? new(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty)),
            Controller.ChangeDraft,
            Theme.Ink,
            "instances.settings").Visible(State.Action == InstanceAction.Configure && State.Draft is not null),
        Column([
            Text(Resources.InstanceBackups)
                .Foreground(Theme.Ink).FontSize(12f),
            LauncherControls.Choice(Theme,BackupNames).MinHeight(40f)
                .SelectedIndex(State.BackupIndex)
                .OnSelectionChanged(Controller.SelectBackup)
                .Enabled(!State.Active && State.Backups.Count > 0 && !State.Succeeded)
                .Automation(new()
                {
                    Name = Resources.InstanceBackups,
                    AutomationId = "instances.operation.backups"
                })
                .Visible(State.Backups.Count > 0),
            Text(State.BackupWarning)
                .Foreground(Theme.Warning)
                .Wrap()
                .Automation(new()
                {
                    Name = State.BackupWarning,
                    AutomationId = "instances.operation.backup-warning",
                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                })
                .Visible(State.BackupReadErrors.Count > 0),
            Text(Resources.InstanceNoBackups)
                .Foreground(Theme.Muted)
                .Wrap()
                .Visible(State.Backups.Count == 0 && State.BackupReadErrors.Count == 0),
        ])
            .Spacing(8f)
            .Visible(State.Action == InstanceAction.Restore),
        Text(ContentReport)
            .FontSize(13f).Foreground(Theme.Ink)
            .Wrap()
            .Automation(new()
            {
                Name = ContentReport,
                AutomationId = "instances.content.summary"
            })
            .Visible(State.Action == InstanceAction.Content && ContentReport.Length > 0),
    ])
        .Spacing(14f)
        .Padding(new Thickness(24f, 20f, 24f, 20f));
}