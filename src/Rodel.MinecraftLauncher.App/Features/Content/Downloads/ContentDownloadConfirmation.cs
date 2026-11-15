// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Input;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal static class ContentDownloadConfirmation
{
    internal static Task<bool> CancelAsync(Window window, string name) => ShowAsync(window, Resources.ContentDownloadCancelTitle,
        string.Format(Resources.ContentDownloadCancelMessage, name), Resources.ContentDownloadCancelAction, "content.download.cancel");

    internal static Task<bool> ExitAsync(Window window) => ShowAsync(window, Resources.ContentDownloadExitTitle,
        Resources.ContentDownloadExitMessage, Resources.ContentDownloadExitAction, "content.download.exit");

    private static async Task<bool> ShowAsync(Window window, string title, string message, string action, string id)
    {
        var confirmed = false;
        ContentDialog? dialog = null;
        var content = new ContentDownloadConfirmationBody(title, message, action, id, value => { confirmed = value; dialog!.Hide(); });
        dialog = new ContentDialog
        {
            ContentComponent = () => content,
            InitialFocus = () => content.Interaction is { } root ? FocusTraversal.Next(root, current: null, forward: false) : null,
        }.Style(LauncherMenus.Confirmation.ContentPadding(0f).MinWidth(320f))
            .Automation(new() { Name = title, AutomationId = id + ".dialog", IsDialog = true });
        await dialog.ShowAsync(window);
        return confirmed;
    }
}

internal sealed partial class ContentDownloadConfirmationBody(string title, string message, string action, string id, Action<bool> decide) : Control
{
    private Ui Body => Column([
        Column([
            Text(title).FontSize(20f).FontWeight(600).Foreground(LauncherTheme.Ink).Wrap(),
            Text(message).FontSize(13f).Foreground(LauncherTheme.Ink).Wrap(),
        ]).Spacing(12f).Padding(24f).MaxWidth(460f),
        Rectangle(LauncherTheme.Edge).Height(1f),
        Grid([GridLength.Star(), GridLength.Star()], [GridLength.Auto], [
            LauncherControls.OutlineCommand(action, () => decide(true)).Style(LauncherButtons.DialogSecondary)
                .Automation(new() { Name = action, AutomationId = id + ".confirm" }).Cell(0, 0),
            LauncherControls.Command(Resources.DialogCancel, () => decide(false)).Style(LauncherButtons.DialogPrimary).AsPrimary()
                .Automation(new() { Name = Resources.DialogCancel, AutomationId = id + ".keep" }).Cell(1, 0),
        ]).ColumnSpacing(8f).Padding(24f, 18f, 24f, 24f),
    ]);
}