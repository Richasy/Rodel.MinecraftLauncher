// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Localization;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Updates;

internal sealed partial class UpdateDialog(
    UpdateState state,
    Window window,
    Action close,
    Func<string, string, Task<bool>> install) : Control
{
    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Star(), GridLength.Auto],
        [
            Column([
                Text(Resources.AppUpdateAvailable).FontSize(20f).FontWeight(600).Foreground(LauncherTheme.Ink).Wrap(),
                Text(string.Format(Resources.AppUpdateVersion, state.Available?.Manifest.Version))
                    .FontSize(13f).Foreground(LauncherTheme.Muted)
                    .Automation(new() { AutomationId = "app.update.version" }),
            ]).Spacing(8f).Padding(24f).Cell(0, 0),
            ScrollView(new UpdateNotesView(LauncherAppearance.Capture(), state.Available?.Notes(Localizer.CurrentLanguage) ?? string.Empty))
                .Padding(new Thickness(24f, 0f, 24f, 16f)).Cell(0, 1),
            Column([
                Text(Resources.AppUpdateInstallHint).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap(),
                ProgressBar().Indeterminate(false)
                    .Value(state.Available is { } update ? state.Received * 100d / update.Installer.Size : 0d)
                    .Thickness(4f).Visible(state.Downloading)
                    .Automation(new() { Name = Resources.AppUpdateDownloading, AutomationId = "app.update.progress" }),
                Text(string.Format(Resources.AppUpdateDownloadProgress, state.Received / 1048576d, (state.Available?.Installer.Size ?? 0) / 1048576d))
                    .FontSize(12f).Foreground(LauncherTheme.Muted).Visible(state.Downloading)
                    .Automation(new() { AutomationId = "app.update.bytes" }),
                Text(state.Blocked ? Resources.AppUpdateBusy : Resources.AppUpdateFailed)
                    .FontSize(13f).Foreground(LauncherTheme.Error).Wrap().Visible(state.Failed || state.Blocked)
                    .Automation(new() { AutomationId = "app.update.error" }),
                Grid([GridLength.Star(), GridLength.Star(), GridLength.Star()], [GridLength.Auto], [
                    LauncherControls.OutlineCommand(Resources.AppUpdateSkip, () => SkipVersion())
                        .Style(LauncherButtons.DialogSecondary).Enabled(!state.Downloading && !state.Installing)
                        .Automation(new() { Name = Resources.AppUpdateSkip, AutomationId = "app.update.skip" }).Cell(0, 0),
                    LauncherControls.OutlineCommand(state.Downloading ? Resources.DialogCancel : Resources.AppUpdateLater, close)
                        .Style(LauncherButtons.DialogSecondary).Enabled(!state.Installing)
                        .Automation(new() { Name = state.Downloading ? Resources.DialogCancel : Resources.AppUpdateLater, AutomationId = "app.update.later" }).Cell(1, 0),
                    LauncherControls.Command(Resources.AppUpdateNow, () => StartUpdate())
                        .Style(LauncherButtons.DialogPrimary).AsPrimary().Enabled(!state.Downloading && !state.Installing)
                        .Automation(new() { Name = Resources.AppUpdateNow, AutomationId = "app.update.install" }).Cell(2, 0),
                ]).ColumnSpacing(8f),
            ]).Spacing(12f).Padding(24f).Cell(0, 2),
        ])
        .Width(Math.Min(580f, window.ClientSize.Width - 48f))
        .Height(Math.Min(520f, window.ClientSize.Height - 48f));

    private void SkipVersion()
    {
        if (state.Skip())
            close();
    }

    private async void StartUpdate()
    {
        var path = await state.DownloadAsync();
        if (path is null)
            return;
        await state.InstallAsync(path, install);
    }
}

[View]
internal readonly partial record struct UpdateNotesView(LauncherAppearance Theme, string Notes)
{
    /// <summary>Shows localized plain-text release notes inside the dialog's independently scrolling body.</summary>
    public Ui Body => Column([
        Text(Notes).FontSize(14f).Foreground(Theme.Ink).Wrap()
            .Automation(new() { AutomationId = "app.update.notes" }),
    ]);
}
