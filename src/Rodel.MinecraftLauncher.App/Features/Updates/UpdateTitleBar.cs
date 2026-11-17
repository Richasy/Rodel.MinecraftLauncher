// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Localization;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Updates;

/// <summary>Hosts the update indicator in Sprout's caption-excluded band without replacing system caption buttons.</summary>
internal sealed partial class UpdateTitleBar(Window window, UpdateState state, Func<string, string, Task<bool>> install) : Control
{
    private bool _dialogOpen;
    private bool _disposed;

    private Ui Body => Row([
        Button(Row([LauncherIcons.Line("download", 18f)]), Show)
            .Style(LauncherButtons.IconButton)
            .Width(40f).Height(32f)
            .ToolTip(Resources.AppUpdateAvailable)
            .Automation(new() { Name = Resources.AppUpdateAvailable, AutomationId = "app.update.open" })
            .Visible(state.Available is not null),
    ]).HAlign(Localizer.CurrentLanguage == "ar" ? HorizontalAlignment.Left : HorizontalAlignment.Right).VAlign(VerticalAlignment.Center);

    private async void Show()
    {
        if (_dialogOpen || state.Available is null || ModalPopup.IsOpen(window))
            return;
        _dialogOpen = true;
        ContentDialog? dialog = null;
        var body = new UpdateDialog(state, window, () => dialog!.Hide(), install);
        dialog = new ContentDialog
        {
            ContentComponent = () => body,
            InitialFocus = () => body.Interaction is { } root ? FocusTraversal.Next(root, current: null, forward: false) : null,
        }.Style(LauncherMenus.Confirmation.ContentPadding(0f).MinWidth(320f))
            .Automation(new() { Name = Resources.AppUpdateAvailable, AutomationId = "app.update.dialog", IsDialog = true });
        dialog.Closing += args =>
        {
            if (state.Installing)
                args.Cancel = true;
            else
                state.Cancel();
        };
        try { await dialog.ShowAsync(window); }
        catch (Exception)
        {
            if (!_disposed)
                LauncherToast.ShowTransientError(window, Resources.AppUpdateFailed);
        }
        finally { _dialogOpen = false; }
    }

    protected override IDisposable? OnMounted()
    {
        _ = state.CheckAsync();
        return null;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        state.Dispose();
        base.OnDispose();
    }
}
