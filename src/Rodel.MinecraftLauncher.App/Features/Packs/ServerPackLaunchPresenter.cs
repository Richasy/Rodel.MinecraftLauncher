// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Popups;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.App.Features.Packs;

/// <summary>Coordinates HMCL review before a native connection through the existing typed pack tasks.</summary>
internal sealed class ServerPackLaunchPresenter(LauncherClient client, HomeController home, Window window)
{
    internal async Task<bool> PrepareAsync(LaunchTask request, CancellationToken cancellationToken)
    {
        if (!client.Packs.IsManaged(request.InstanceId))
            return true;
        Exception? readError = null;
        try
        {
            var state = await Task.Run(() => client.Packs.GetState(request.InstanceId), cancellationToken);
            var pack = state.Pack;
            if (pack?.Format != "hmcl" || pack.SourceDetached == true || !pack.HasDistributionSource)
            {
                return true;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            readError = exception;
        }

        if (ModalPopup.IsOpen(window))
            throw new InvalidOperationException(Resources.InstanceBusyConflict);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        var connect = false;
        var controller = new PackMaintenanceController(client, home, request.InstanceId);
        if (readError is not null)
            controller.ReportError(readError);
        PackMaintenanceDialog? content = null;
        var popup = new Flyout(close => content ??= new PackMaintenanceDialog(controller, window, close, () =>
                {
                    connect = true;
                    close();
                }))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(PackMaintenanceDialog.MaximumWidth).Padding(0f)).Automation(new()
        {
            Name = Resources.PackUpdateHeading,
            AutomationId = "pack.update.popup",
            IsDialog = true
        });
        popup.Closing += args =>
        {
            args.Cancel = content?.CanClose == false;
            if (args.Cancel)
                content?.RequestClose();
        };
        popup.Closed += () => completion.TrySetResult(connect);
        using var work = home.TrackManagementWork();
        if (!ModalPopup.TryShow(window, popup))
        {
            controller.Dispose();
            throw new InvalidOperationException(Resources.InstanceBusyConflict);
        }

        return await completion.Task;
    }
}