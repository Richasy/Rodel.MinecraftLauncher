// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed partial class DiagnosticsSection(LauncherClient client, HomeController home, Window window) : Control
{
    private Flyout? _popup;
    private PopupFocusRecovery? _focus;
    private partial bool Compact { get; set; }

    private Ui Body => Column([
        new DiagnosticsExportRow(LauncherAppearance.Capture(),true, Compact, () => ShowExport(true)),
        new DiagnosticsExportRow(LauncherAppearance.Capture(),false, Compact, () => ShowExport(false)),
    ]).Spacing(20f);

    private void ShowExport(bool launcher)
    {
        if (ModalPopup.IsOpen(window))
            return;
        var returnFocus = window.FocusedElement;
        DiagnosticsExportDialog? content = null;
        _popup = new Flyout(close => content = new(client, home, window, close, launcher))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup).Automation(new()
        {
            Name = launcher ? Resources.LauncherLogsTitle : Resources.GameLogsTitle,
            AutomationId = "diagnostics.popup",
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
            _focus?.Request(this, returnFocus);
        };
        ModalPopup.TryShow(window, _popup);
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs args) => Compact = args.Size.Width < 1100f;

    protected override IDisposable? OnMounted()
    {
        _focus = new(window);
        Compact = window.ClientSize.Width < 1100f;
        window.SizeChanged += OnSizeChanged;
        return null;
    }

    protected override void OnDispose()
    {
        window.SizeChanged -= OnSizeChanged;
        _focus?.Dispose();
        base.OnDispose();
    }
}

[View]
internal readonly partial record struct DiagnosticsExportRow(LauncherAppearance Theme, bool Launcher, bool Compact, Action Open)
{
    internal GridLength[] ColumnDefinitions => Compact ? [GridLength.Star()] : [GridLength.Star(), GridLength.Auto];
    internal GridLength[] RowDefinitions => Compact ? [GridLength.Auto, GridLength.Auto] : [GridLength.Auto];

    /// <summary>Presents one export scope and opens its independent dialog.</summary>
    public Ui Body => Grid(
        ColumnDefinitions,
        RowDefinitions,
        [
            Column([
                Text(Launcher ? Resources.LauncherLogsTitle : Resources.GameLogsTitle)
                    .FontSize(14f).Foreground(Theme.Ink),
                Text(Launcher ? Resources.LauncherLogsHelp : Resources.GameLogsHelp)
                    .FontSize(12f).Foreground(Theme.Muted)
                    .Wrap(),
            ])
                .Spacing(6f)
                .VAlign(VerticalAlignment.Center)
                .Cell(0, 0),
            LauncherControls.OutlineCommand(Theme,Resources.DiagnosticsOpen, Open)
                .MinHeight(40f)
                .VAlign(VerticalAlignment.Center)
                .Automation(new()
                {
                    Name = Resources.DiagnosticsOpen,
                    AutomationId = Launcher ? "settings.diagnostics.open" : "settings.game-logs.open"
                })
                .Cell(Compact ? 0 : 1, Compact ? 1 : 0),
        ])
        .ColumnSpacing(16f).RowSpacing(10f);
}