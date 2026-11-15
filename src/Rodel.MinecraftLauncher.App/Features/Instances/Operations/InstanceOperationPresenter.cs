// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Menus;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Popups;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

/// <summary>Owns the window's shared instance operation dialogs and menu commands, independently of either list page.</summary>
internal sealed partial class InstanceOperationPresenter : IDisposable
{
    private readonly LauncherClient _client;
    private readonly HomeController _home;
    private readonly Window _window;
    private readonly InstanceManagementController _operations;
    private readonly CancellationTokenSource _lifetime = new();
    private Flyout? _popup;
    private MenuFlyout? _contextMenu;

    internal InstanceOperationPresenter(LauncherClient client, HomeController home, Window window)
    {
        _client = client;
        _home = home;
        _window = window;
        _operations = new(client, home);
    }

    internal bool Opening { get; private set; }
    internal bool ConfigurationOpen => _popup?.IsOpen == true && _operations.State.Action == InstanceAction.Configure;

    internal Task<bool> FlushConfigurationAsync() => ConfigurationOpen
        ? _operations.FlushConfigurationAsync()
        : Task.FromResult(true);

    internal MenuFlyoutItem RenameItem(string id, string name, Action? closed = null) =>
        Command(Resources.InstanceRenameHeading, InstanceAction.Rename, "rename", id, name, closed);

    internal MenuFlyoutItem IconItem(string id, string name, Action? closed = null) =>
        Command(Resources.InstanceIcon, InstanceAction.Icon, "icon", id, name, closed);

    internal CriticalMenuItem DeleteItem(string id, string name, Action? closed = null) => new(
        Resources.InstanceRemoveHeading, () => Open(id, InstanceAction.Delete, closed))
    {
        Automation = new()
        {
            Name = string.Format(Resources.InstanceManageAction, Resources.InstanceRemoveHeading, name),
            AutomationId = "instances.menu.delete"
        }
    };

    private MenuFlyoutItem Command(string title, InstanceAction action, string suffix, string id, string name, Action? closed) => new(
        title, () => Open(id, action, closed))
    {
        Automation = new()
        {
            Name = string.Format(Resources.InstanceManageAction, title, name),
            AutomationId = "instances.menu." + suffix
        }
    };

    internal MenuFlyout? ContextMenu(string id, Action? closed = null)
    {
        if (_lifetime.IsCancellationRequested || _home.State.LocalInstances.FirstOrDefault(item => item.Id == id) is not { } instance)
            return null;
        _contextMenu?.Hide();
        var delete = DeleteItem(id, instance.Name, closed);
        _contextMenu = new MenuFlyout(
            RenameItem(id, instance.Name, closed),
            IconItem(id, instance.Name, closed),
            new MenuFlyoutSeparator(),
            delete)
            .Style(LauncherMenus.CommandMenu)
            .Automation(new()
            {
                Name = string.Format(Resources.InstanceManageContext, instance.Name),
                AutomationId = "home.instances.menu"
            });
        _contextMenu.Closed += delete.Dispose;
        return _contextMenu;
    }

    internal async void Open(string id, InstanceAction action, Action? closed = null)
    {
        if (action == InstanceAction.Icon)
        {
            await ShowIconAsync(id, closed);
            return;
        }

        if (_lifetime.IsCancellationRequested || ModalPopup.IsOpen(_window) || _popup?.IsOpen == true || _operations.State.Active || Opening)
            return;
        Opening = true;
        try
        {
            try
            {
                await _operations.OpenAsync(id, action);
            }
            catch (Exception exception)
            {
                if (action == InstanceAction.Rename || _operations.State.Instance is null)
                {
                    LauncherToast.ShowError(_window, exception.Message);
                    return;
                }

                _operations.ReportError(exception);
            }

            if (_lifetime.IsCancellationRequested || ModalPopup.IsOpen(_window))
                return;
            InstanceOperationPopup? content = null;
            var accessibleName = _operations.State.Instance is { } instance
                ? string.Format(Resources.InstanceManageAction, _operations.State.Title, instance.Name)
                : _operations.State.Title;
            _popup = new Flyout(close => content = new InstanceOperationPopup(_operations, _window, close))
            {
                Placement = PopupPlacement.Full,
                IsModal = action != InstanceAction.Configure,
                IsLightDismissEnabled = action == InstanceAction.Configure
            }.LauncherStyle(appearance => appearance.Menus.ErrorPopup).Automation(new()
            {
                Name = accessibleName,
                AutomationId = "instances.operation.popup",
                IsDialog = true
            });
            _popup.Closing += e =>
            {
                e.Cancel = action == InstanceAction.Configure
                    ? content?.ConfigurationCloseReady != true
                    : content?.CanClose == false;
                if (e.Cancel)
                    content?.RequestClose();
            };
            _popup.Closed += () => closed?.Invoke();
            ModalPopup.TryShow(_window, _popup);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_lifetime.IsCancellationRequested)
                LauncherToast.ShowError(_window, exception.Message);
        }
        finally
        {
            Opening = false;
        }
    }

    /// <summary>Releases the shared operation observer and closes only this window's owned instance popups.</summary>
    public void Dispose()
    {
        _lifetime.Cancel();
        _operations.Dispose();
        _contextMenu?.Hide();
        _popup?.Hide();
        _lifetime.Dispose();
    }
}