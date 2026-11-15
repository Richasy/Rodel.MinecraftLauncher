// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Widgets;
using Sprout.WindowsAppSDK;
using System.Text;
using static Sprout.Markup;
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(string[]))]

namespace Rodel.MinecraftLauncher.App.Controls.Reports;

internal sealed partial class DetailReportPopup : Control
{
    private readonly Window _window;
    private readonly string _title;
    private readonly Action _close;
    private readonly Action? _back;
    private readonly Func<bool>? _canClose;
    private readonly bool _showCloseButton;
    private readonly float _contentWidth;
    private readonly string _automationPrefix;
    private readonly string? _accessibleName;
    private readonly Action<bool>? _exportStateChanged;
    private readonly PopupFocusRecovery _focus;
    private readonly Action? _findDependencies;
    private readonly Action? _repair;

    internal DetailReportPopup(
        Window window,
        string title,
        string report,
        Action close,
        float contentWidth,
        string automationPrefix = "launch.error",
        string? accessibleName = null,
        Action<bool>? exportStateChanged = null,
        Action? back = null,
        Func<bool>? canClose = null,
        bool showCloseButton = true,
        Action? findDependencies = null,
        Action? repair = null)
    {
        _window = window;
        _focus = new(window);
        _title = title;
        _close = close;
        _back = back;
        _canClose = canClose;
        _showCloseButton = showCloseButton;
        _contentWidth = contentWidth;
        _automationPrefix = automationPrefix;
        _accessibleName = accessibleName;
        _exportStateChanged = exportStateChanged;
        _findDependencies = findDependencies;
        _repair = repair;
        Report = report;
        RawReport = string.Empty;
        ContentReady = true;
    }

    internal partial string Report { get; private set; }
    internal partial bool ContentReady { get; set; }
    /// <summary>Gets or sets the parent page's height snapshot, bounded by the reading minimum and current window.</summary>
    public partial float ViewHeight { get; set; }
    /// <summary>Gets or sets the parent page's width snapshot, bounded by the current window.</summary>
    public partial float ViewWidth { get; set; }
    internal partial string RawReport { get; private set; }
    public partial bool RawMode { get; private set; }
    private partial string RawSnapshot { get; set; }
    private string DisplayReport => RawMode ? RawSnapshot : Report;

    internal void SetReport(string report, string rawReport = "")
    {
        if (Exporting || (Report == report && RawReport == rawReport))
            return;
        Report = report;
        RawReport = rawReport;
        if (rawReport.Length == 0)
            RawMode = false;
        Copied = false;
        ExportPath = string.Empty;
        OperationError = string.Empty;
    }
    private bool _disposed;
    public partial bool Exporting { get; private set; }
    public partial string OperationError { get; private set; }
    public partial bool Copied { get; private set; }
    public partial string ExportPath { get; private set; }
    private bool FixedViewport => ViewHeight > 0f;
    private float MaximumHeight => Math.Max(120f, _window.ClientSize.Height - 48f);
    private float DialogHeight => Math.Min(MaximumHeight, Math.Max(360f, FixedViewport ? ViewHeight : 480f));
    private static GridLength[] DialogRows => [GridLength.Auto, GridLength.Star(), GridLength.Auto];
    private static GridLength[] ReportRows => [GridLength.Star(), GridLength.Auto];
    private static TextBoxPalette ReportPalette => LauncherStyles.InputPalette with
    {
        Background = Colors.Transparent,
        BackgroundFocused = Colors.Transparent,
        BackgroundPointerOver = Colors.Transparent,
        RestingElevation = ElevationProfile.TextField(Colors.Transparent, Colors.Transparent),
        FocusedElevation = ElevationProfile.TextField(Colors.Transparent, Colors.Transparent),
    };

    private Ui Body => Grid(
        [GridLength.Star()],
        DialogRows,
        [
            Grid(
                [GridLength.Auto, GridLength.Star(), GridLength.Auto],
                [GridLength.Auto],
                [
                    LauncherControls.Command(
                        Row([
                            LauncherIcons.Line(_window.FlowDirection == FlowDirection.RightToLeft ? "forward" : "back", 18f)
                        ]),
                        _back)
                        .Style(LauncherButtons.IconButton)
                        .Width(32f).Height(32f)
                        .VAlign(VerticalAlignment.Center)
                        .Enabled(!Exporting)
                        .Visible(_back is not null)
                        .Automation(new()
                        {
                            Name = Resources.BackToOperation,
                            AutomationId = _automationPrefix + ".back"
                        })
                        .Cell(0, 0),
                    Text(_title)
                        .FontSize(20f).FontWeight(700).Foreground(LauncherTheme.Ink)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .VAlign(VerticalAlignment.Center)
                        .Cell(1, 0),
                    Row([
                        Button(RawMode ? Resources.ReportReadableShort : Resources.ReportRawShort, () => ToggleRaw())
                            .Style(LauncherButtons.DialogHeaderButton)
                            .VAlign(VerticalAlignment.Center)
                            .Enabled(!Exporting)
                            .Visible(RawReport.Length > 0)
                            .Automation(new()
                            {
                                Name = RawMode ? Resources.ViewReadableReport : Resources.ViewRawJson,
                                AutomationId = _automationPrefix + ".raw"
                            }),
                    ])
                        .Spacing(0f)
                        .Cell(2, 0),
                ])
                .ColumnSpacing(10f)
                .Padding(24f, 20f, 24f, 16f)
                .Cell(0, 0),
            Grid(
                [GridLength.Star()],
                [GridLength.Star(), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto],
                [
                    Border(Grid(
                            [GridLength.Star()],
                            ReportRows,
                            [
                                Flow(FlowDirection.LeftToRight, TextBox()
                                        .Text(DisplayReport)
                                        .ReadOnly()
                                        .AcceptsReturn()
                                        .Wrap()
                                        .FlowDirection(RawMode ? FlowDirection.LeftToRight : _window.FlowDirection)
                                        .Style(LauncherStyles.Input.ColorPalette(ReportPalette))
                                        .Padding(12f, 8f)
                                        .MinHeight(40f)
                                        .Automation(new()
                                        {
                                            Name = _accessibleName ?? Resources.FullLaunchError,
                                            AutomationId = _automationPrefix + ".text"
                                        }))
                                    .Cell(0, 0),
                                Border(Row([
                                        Button(Resources.CopyLaunchError, () => CopyReport())
                                            .Style(LauncherButtons.IconButton.Padding(8f, 4f)
                                                .LabelStyle(LauncherStyles.Text.FontSize(12f)))
                                            .MinHeight(32f)
                                            .Enabled(ContentReady && !Exporting)
                                            .Automation(new()
                                            {
                                                Name = Resources.CopyLaunchError,
                                                AutomationId = _automationPrefix + ".copy"
                                            }),
                                        Button(Resources.ExportLaunchError, () => _ = ExportAsync())
                                            .Style(LauncherButtons.IconButton.Padding(8f, 4f)
                                                .LabelStyle(LauncherStyles.Text.FontSize(12f)))
                                            .MinHeight(32f)
                                            .Enabled(ContentReady && !Exporting)
                                            .Automation(new()
                                            {
                                                Name = Resources.ExportLaunchError,
                                                AutomationId = _automationPrefix + ".export"
                                            }),
                                    ])
                                        .Spacing(8f))
                                    .Padding(8f, 6f)
                                    .BorderBrush(LauncherTheme.Edge)
                                    .BorderThickness(0f, 1f, 0f, 0f)
                                    .Cell(0, 1),
                            ]))
                        .Background(LauncherTheme.ControlBackground)
                        .BorderBrush(LauncherTheme.Edge)
                        .BorderThickness(1f)
                        .Cell(0, 0),
                    Text(Copied ? Resources.LaunchErrorCopied : Resources.RawJsonSnapshotHint)
                        .FontSize(11f).Foreground(LauncherTheme.Muted)
                        .Visible((Copied || RawMode) && string.IsNullOrEmpty(ExportPath))
                        .Margin(new Thickness(0f, 8f, 0f, 0f))
                        .Automation(new()
                        {
                            Name = Copied ? Resources.LaunchErrorCopied : Resources.RawJsonSnapshotHint,
                            AutomationId = _automationPrefix + ".copied"
                        })
                        .Cell(0, 1),
                    new PathSummaryView(Resources.ReportExportedLabel, ExportPath ?? string.Empty, _automationPrefix + ".exported", Appearance: PathSummaryAppearance.Capture()).Visible(!string.IsNullOrEmpty(ExportPath))
                        .Margin(new Thickness(0f, 8f, 0f, 0f))
                        .Cell(0, 2),
                    Text(string.Format(Resources.LaunchErrorDetail, OperationError ?? string.Empty))
                        .FontSize(12f).Foreground(LauncherTheme.Error)
                        .Wrap()
                        .Visible(!string.IsNullOrEmpty(OperationError))
                        .Margin(new Thickness(0f, 8f, 0f, 0f))
                        .Cell(0, 3),
                    LauncherControls.OutlineCommand(Resources.FindMissingDependencies, _findDependencies)
                        .Enabled(!Exporting)
                        .Visible(_findDependencies is not null)
                        .Automation(new()
                        {
                            Name = Resources.FindMissingDependencies,
                            AutomationId = _automationPrefix + ".dependencies"
                        })
                        .Margin(new Thickness(0f, 8f, 0f, 0f))
                        .Cell(0, 4),
                ])
                .Padding(24f, 0f, 24f, 20f)
                .Cell(0, 1),
            Border(Grid(
                    [GridLength.Star(), GridLength.Star()],
                    [GridLength.Auto],
                    [
                        LauncherControls.Command(Resources.AiRepairStart, _repair)
                            .Style(LauncherButtons.DialogPrimary)
                            .Enabled(!Exporting)
                            .Visible(_repair is not null)
                            .Automation(new()
                            {
                                Name = Resources.AiRepairStart,
                                AutomationId = _automationPrefix + ".ai.repair"
                            })
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(Resources.CloseLaunchSettings, _close)
                            .Style(LauncherButtons.DialogSecondary)
                            .Enabled(!Exporting && (_canClose?.Invoke() ?? true))
                            .Automation(new()
                            {
                                Name = Resources.CloseLaunchSettings,
                                AutomationId = _automationPrefix + (_showCloseButton ? ".close" : ".dismiss")
                            })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(12f))
                .Padding(24f, 16f, 24f, 20f)
                .Background(LauncherTheme.ControlBackground)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 1f, 0f, 0f)
                .Cell(0, 2),
        ])
        .Width(ViewWidth > 0f ? Math.Min(ViewWidth, _window.ClientSize.Width - 48f) : Math.Min(_contentWidth + 24f, _window.ClientSize.Width - 48f))
        .Height(DialogHeight);

    private void ToggleRaw()
    {
        if (!RawMode)
            RawSnapshot = RawReport;
        RawMode = !RawMode;
        Copied = false;
        ExportPath = string.Empty;
        OperationError = string.Empty;
    }

    private void CopyReport()
    {
        if (!ContentReady || Exporting)
            return;
        Copied = Sprout.Host.Clipboard.SetText(DisplayReport);
        if (Copied)
            ExportPath = string.Empty;
        OperationError = Copied ? string.Empty : Resources.LaunchErrorCopyFailed;
    }

    private async Task ExportAsync()
    {
        if (!ContentReady || Exporting)
            return;
        var returnFocus = _window.FocusedElement;
        Exporting = true;
        _exportStateChanged?.Invoke(true);
        OperationError = string.Empty;
        try
        {
            var picker = _window.CreateFileSavePicker();
            picker.SuggestedFileName = "launcher-error-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string[] extensions = [".txt"];
            picker.FileTypeChoices.Add(Resources.ErrorTextFile, extensions);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;
            var temporary = file.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, DisplayReport, new UTF8Encoding(false));
                File.Move(temporary, file.Path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }

            if (!_disposed)
            {
                Copied = false;
                ExportPath = file.Path;
            }
        }
        catch (Exception exception)
        {
            if (!_disposed)
                OperationError = exception.Message;
        }
        finally
        {
            _exportStateChanged?.Invoke(false);
            if (!_disposed)
            {
                Exporting = false;
                _focus.Request(this, returnFocus);
            }
        }
    }

    protected override IDisposable? OnMounted() => _back is null ? PopupKeyboardClose.Bind(_window, _close, () => !Exporting && (_canClose?.Invoke() ?? true)) : null;

    protected override void OnDispose()
    {
        _disposed = true;
        _focus.Dispose();
        base.OnDispose();
    }
}