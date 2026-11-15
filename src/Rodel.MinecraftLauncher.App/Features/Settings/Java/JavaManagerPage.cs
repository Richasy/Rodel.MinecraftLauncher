// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

/// <summary>Lists actual Java installations. Acquisition and removal have focused dialogs.</summary>
internal sealed partial class JavaManagerPage : Control
{
    private readonly JavaManagerController _controller;
    private readonly Window _window;
    private readonly JavaInstallationList _list;
    private readonly LauncherSettingsCard _defaultCard;
    private readonly PopupFocusRecovery _focus;
    private Flyout? _popup;
    private MenuFlyout? _addMenu;
    private MenuFlyout? _entryMenu;
    private InteractionNode? _addTrigger;
    private InteractionNode? _entryTrigger;

    internal JavaManagerPage(
        LauncherClient client,
        HomeController home,
        CorePreferencesStore preferences,
        Window window)
    {
        _controller = new(client, home, preferences);
        _window = window;
        _list = new(_controller);
        _defaultCard = new(Resources.SettingsJavaDefault, new JavaDefaultSetting(this), window, () => Resources.SettingsJavaDefault);
        _focus = new(window);
        State = _controller.State;
        ViewportHeight = window.ClientSize.Height;
        _controller.Changed += OnChanged;
    }

    internal partial JavaManagerState State { get; private set; }
    private partial float ViewportHeight { get; set; }
    private partial bool DialogOpen { get; set; }
    internal string DefaultLabel => State.DefaultLabel;

    internal void UseAutomatic() => _ = _controller.UseAsync(true);
    private string CountText => string.Format(Resources.JavaLocalCount, State.Entries.Length);
    private bool IsSelectedDefault => State.Entry is { } entry && (entry.Id == State.Default || string.Equals(entry.Path, State.Default, StringComparison.OrdinalIgnoreCase));

    internal Task OpenAsync() => _controller.RefreshAsync();

    private void OnChanged(JavaManagerState state) => State = state;

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Auto, GridLength.Star(), GridLength.Auto],
        [
            _defaultCard.Cell(0, 0),
            Grid(
                [GridLength.Star(), GridLength.Auto, GridLength.Auto],
                [GridLength.Auto],
                [
                    Column([
                        Text(CountText)
                            .FontSize(18f).FontWeight(600).Foreground(LauncherTheme.Ink),
                        Text(Resources.SettingsJavaListHelp)
                            .FontSize(12f).Foreground(LauncherTheme.Muted)
                            .Wrap(),
                    ])
                        .Spacing(6f)
                        .VAlign(VerticalAlignment.Center)
                        .Cell(0, 0),
                    LauncherControls.Command(
                        Row([
                            LauncherIcons.Line("refresh", 18f)
                        ]),
                        () => _ = _controller.RefreshAsync())
                        .Style(LauncherButtons.IconButton)
                        .Width(36f).Height(36f)
                        .Enabled(State.Editable)
                        .ToolTip(Resources.JavaScan)
                        .Automation(new()
                        {
                            Name = Resources.JavaScan,
                            AutomationId = "java.scan"
                        })
                        .Cell(1, 0),
                    LauncherControls.OutlineCommand(Resources.JavaAdd, () => ShowAddMenu())
                        .MinHeight(36f)
                        .Enabled(State.Editable)
                        .Automation(new()
                        {
                            Name = Resources.JavaAdd,
                            AutomationId = "java.add"
                        })
                        .Cell(2, 0),
                ])
                .ColumnSpacing(8f)
                .Margin(new Thickness(0f, 24f, 0f, 12f))
                .Cell(0, 1),
            Border(Overlay([
                    _list.WithState(State)
                        .Visible(State.Entries.Length > 0),
                    Border(Column([
                            ProgressBar()
                                .Indeterminate(true)
                                .Thickness(3f)
                                .Visible(State.Loading),
                            Text(State.Loading ? Resources.JavaScanning : Resources.JavaNone)
                                .FontSize(14f).Foreground(LauncherTheme.Muted)
                                .Wrap()
                                .Automation(new()
                                {
                                    Name = State.Loading ? Resources.JavaScanning : Resources.JavaNone,
                                    AutomationId = "java.empty",
                                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                                }),
                        ])
                            .Spacing(12f))
                        .Padding(20f)
                        .VAlign(VerticalAlignment.Center)
                        .Visible(State.Entries.Length == 0),
                ]))
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(1f)
                .Background(LauncherTheme.ControlBackground)
                .Cell(0, 2),
            Column([
                ProgressBar()
                    .Indeterminate(true)
                    .Thickness(3f)
                    .Visible(State.Loading && State.Entries.Length > 0),
                Row([
                    Text(Resources.JavaManageFailed)
                        .FontSize(12f).Foreground(LauncherTheme.Error)
                        .Wrap()
                        .Weight(1f)
                        .Automation(new()
                        {
                            Name = Resources.JavaManageFailed,
                            AutomationId = "java.error",
                            LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                        }),
                    LauncherControls.Command(
                        Row([
                            LauncherIcons.Line("info", 18f)
                        ]),
                        () => ShowFailure())
                        .Style(LauncherButtons.IconButton)
                        .Width(32f).Height(32f)
                        .ToolTip(Resources.JavaDetails)
                        .Automation(new()
                        {
                            Name = Resources.JavaDetails,
                            AutomationId = "java.error.details"
                        }),
                ])
                    .Spacing(8f)
                    .Visible(State.Error.Length > 0 && !DialogOpen),
                Row([
                    LauncherControls.Command(Resources.JavaUseDefault, () => _ = _controller.UseAsync())
                        .Style(LauncherButtons.DialogPrimary)
                        .MinHeight(36f)
                        .Enabled(State.Editable && State.Entry is { Usable: true })
                        .Visible(!IsSelectedDefault)
                        .Automation(new()
                        {
                            Name = Resources.JavaUseDefault,
                            AutomationId = "java.use-default",
                            HelpText = State.Entry?.Path
                        }),
                    Text(Resources.JavaCurrentDefault)
                        .FontSize(13f).Foreground(LauncherTheme.Muted)
                        .VAlign(VerticalAlignment.Center)
                        .Visible(IsSelectedDefault),
                    LauncherControls.OutlineCommand(Resources.JavaMore, () => ShowEntryMenu())
                        .MinHeight(36f)
                        .Enabled(State.Editable)
                        .Automation(new()
                        {
                            Name = Resources.JavaMore,
                            AutomationId = "java.more",
                            HelpText = State.Entry?.Path
                        }),
                ])
                    .Spacing(10f)
                    .Visible(State.Entry is not null),
            ])
                .Spacing(8f)
                .Margin(new Thickness(0f, 10f, 0f, 0f))
                .Cell(0, 3),
        ])
        .Padding(0f, 0f, 24f, 24f);

    private void ShowAddMenu()
    {
        _addTrigger = _window.FocusedElement;
        _addMenu = new MenuFlyout(new MenuFlyoutItem(Resources.JavaAddLocal, () => ShowOperation(JavaOperation.AddLocal))
        {
            Automation = new()
            {
                Name = Resources.JavaAddLocal,
                AutomationId = "java.add.local"
            }
        }, new MenuFlyoutItem(Resources.JavaDownload, () => ShowOperation(JavaOperation.Download))
        {
            Automation = new()
            {
                Name = Resources.JavaDownload,
                AutomationId = "java.add.download"
            }
        }).Style(LauncherMenus.CommandMenu);
        _addMenu.ShowAt(_window);
    }

    private void ShowEntryMenu()
    {
        if (State.Entry is null)
            return;
        _entryTrigger = _window.FocusedElement;
        var items = new List<MenuFlyoutItemBase>
        {
            new MenuFlyoutItem(Resources.JavaDetails, ShowDetails)
            {
                Automation = new()
                {
                    Name = Resources.JavaDetails,
                    AutomationId = "java.details"
                }
            },
        };
        if (State.Entry.Id is not null)
        {
            items.Add(new MenuFlyoutItem(Resources.JavaRemove, () => ShowOperation(JavaOperation.Remove))
            {
                Automation = new()
                {
                    Name = Resources.JavaRemove,
                    AutomationId = "java.remove"
                }
            });
        }

        _entryMenu = new MenuFlyout([.. items]).Style(LauncherMenus.CommandMenu);
        _entryMenu.ShowAt(_window);
    }

    private void ShowOperation(JavaOperation operation)
    {
        if (Controls.ModalPopup.IsOpen(_window) || !State.Editable)
            return;
        var target = State.Entry;
        if (operation == JavaOperation.Remove && target?.Id is null)
            return;
        _controller.BeginOperation();
        var returnFocus = (operation == JavaOperation.Remove ? _entryTrigger : _addTrigger) ?? _window.FocusedElement;
        var title = operation == JavaOperation.Download
            ? Resources.JavaDownload
            : operation == JavaOperation.Remove ? Resources.JavaRemove : Resources.JavaAddLocal;
        JavaOperationDialog? content = null;
        _popup = new Flyout(close => content = new(
                _controller,
                _window,
                operation,
                target,
                close))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup).Automation(new()
        {
            Name = title,
            AutomationId = "java.operation.popup",
            IsDialog = true
        });
        _popup.Closing += e =>
        {
            e.Cancel = content?.CanClose == false;
            if (e.Cancel)
                content?.RequestClose();
        };
        _popup.Closed += () =>
        {
            _popup = null;
            DialogOpen = false;
            _focus.Request(this, returnFocus);
        };
        DialogOpen = Controls.ModalPopup.TryShow(_window, _popup);
        if (operation == JavaOperation.Download)
            _ = _controller.LoadCatalogAsync();
    }

    private void ShowDetails()
    {
        if (Controls.ModalPopup.IsOpen(_window) || State.Entry is not { } entry)
            return;
        ShowReport(entry.Path + "\n\n" + entry.Details, _entryTrigger);
    }

    private void ShowFailure() => ShowReport(State.Error);

    private void ShowReport(string report, InteractionNode? preferredFocus = null)
    {
        if (Controls.ModalPopup.IsOpen(_window))
            return;
        var returnFocus = preferredFocus ?? _window.FocusedElement;
        _popup = new Flyout(close => new DetailReportPopup(
                _window,
                Resources.JavaDetails,
                report,
                close,
                Math.Min(640f, _window.ClientSize.Width - 48f),
                "java.details"))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup);
        _popup.Closed += () =>
        {
            _popup = null;
            DialogOpen = false;
            _focus.Request(this, returnFocus);
        };
        DialogOpen = Controls.ModalPopup.TryShow(_window, _popup);
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs args) => ViewportHeight = args.Size.Height;

    protected override IDisposable? OnMounted()
    {
        _window.SizeChanged += OnSizeChanged;
        return null;
    }

    protected override void OnDispose()
    {
        _controller.Changed -= OnChanged;
        _window.SizeChanged -= OnSizeChanged;
        _controller.Dispose();
        _focus.Dispose();
        _popup?.Hide();
        _addMenu?.Hide();
        _entryMenu?.Hide();
        base.OnDispose();
    }
}