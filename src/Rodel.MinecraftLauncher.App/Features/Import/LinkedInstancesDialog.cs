// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Instances;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Import;

/// <summary>Lets players link a batch of existing versions without moving or copying their game data.</summary>
internal sealed partial class LinkedInstancesDialog : Control
{
    private readonly LauncherClient client;
    private readonly HomeController home;
    private readonly Window window;
    private readonly Action close;
    private LinkedInstancesDialog(LauncherClient client, HomeController home, Window window, Action close)
    {
        this.client = client;
        this.home = home;
        this.window = window;
        this.close = close;
        Source = string.Empty;
        Entries = [];
        Error = string.Empty;
        Viewport = window.ClientSize;
    }

    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _scan;
    private partial string Source { get; set; }
    private partial LinkedInstanceEntry[] Entries { get; set; }
    private partial bool Busy { get; set; }
    private partial bool Scanning { get; set; }
    private partial string Error { get; set; }
    private partial bool HasNoVersions { get; set; }
    private partial Size Viewport { get; set; }
    private bool CanConfirm => !Busy && !Scanning && Entries.Any(entry => entry.Selected)
        && Entries.Where(entry => entry.Selected).All(entry => !entry.Preview.NeedsGameDirectory || entry.GameDirectory is not null);

    internal static void Show(LauncherClient client, HomeController home, Window window)
    {
        if (ModalPopup.IsOpen(window))
            return;
        LinkedInstancesDialog? content = null;
        var popup = new Flyout(close => content = new(client, home, window, close))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false,
        }.LauncherStyle(appearance => appearance.Menus.ErrorPopup)
            .Automation(new() { Name = Resources.LinkExisting, AutomationId = "link.popup", IsDialog = true });
        void ThemeChanged(object? sender, EventArgs args) => popup.LauncherStyle(appearance => appearance.Menus.ErrorPopup);
        window.ActualThemeChanged += ThemeChanged;
        popup.Closed += () => window.ActualThemeChanged -= ThemeChanged;
        popup.Closing += args => args.Cancel = content?.Busy == true;
        if (!ModalPopup.TryShow(window, popup))
            window.ActualThemeChanged -= ThemeChanged;
    }

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Star(), GridLength.Auto],
        [
            Border(Text(Resources.LinkExisting).FontSize(19f).FontWeight(600)
                .Foreground(LauncherTheme.Ink).Wrap()).Padding(24f).Cell(0, 0),
            ScrollView(new LinkedInstancesBody(LauncherAppearance.Capture(), Source, Entries, Busy, Scanning, HasNoVersions, Error,
                ChangeSource, Browse, ToggleAll, UpdateEntry))
                .Cell(0, 1),
            Border(Grid([GridLength.Star(), GridLength.Star()], [GridLength.Auto], [
                LauncherControls.Command(Resources.LinkConfirm, () => _ = ConfirmAsync())
                    .Style(LauncherButtons.DialogPrimary).Enabled(CanConfirm)
                    .Automation(new() { Name = Resources.LinkConfirm, AutomationId = "link.confirm" }).Cell(0, 0),
                LauncherControls.OutlineCommand(Resources.DialogCancel, close)
                    .Style(LauncherButtons.DialogSecondary).Enabled(!Busy)
                    .Automation(new() { Name = Resources.DialogCancel, AutomationId = "link.cancel" }).Cell(1, 0),
            ]).ColumnSpacing(12f))
                .BorderBrush(LauncherTheme.Edge).BorderThickness(0f, 1f, 0f, 0f)
                .Padding(24f).Cell(0, 2),
        ])
        .Width(Math.Min(640f, Viewport.Width - 48f))
        .Height(Math.Min(680f, Viewport.Height - 64f));

    private void UpdateEntry(LinkedInstanceEntry entry, bool selected, string? directory) =>
        Entries = [.. Entries.Select(item => item.Key == entry.Key ? item with { Selected = selected, GameDirectory = directory } : item)];

    private void ToggleAll()
    {
        var selected = !Entries.All(entry => entry.Selected);
        Entries = [.. Entries.Select(entry => entry with { Selected = selected })];
    }

    private void ChangeSource(string value)
    {
        Source = value;
        _scan?.Cancel();
        _scan?.Dispose();
        _scan = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        Entries = [];
        Error = string.Empty;
        HasNoVersions = false;
        Scanning = !string.IsNullOrWhiteSpace(value);
        if (Scanning)
            _ = ScanAsync(value, _scan.Token);
    }

    private async Task ScanAsync(string source, CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            var previews = await Task.Run(() => client.Instances.PreviewLinks(source), token);
            if (token.IsCancellationRequested)
                return;
            Entries = [.. previews.Select(preview => new LinkedInstanceEntry(preview, true, preview.NeedsGameDirectory ? null : preview.GameDirectory))];
            HasNoVersions = Entries.Length == 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { if (!token.IsCancellationRequested) Error = exception.Message; }
        finally { if (!token.IsCancellationRequested) Scanning = false; }
    }

    private async Task BrowseAsync()
    {
        Busy = true;
        try
        {
            var folder = await window.CreateFolderPicker().PickSingleFolderAsync();
            if (!_lifetime.IsCancellationRequested && folder is not null)
                ChangeSource(folder.Path);
        }
        catch (Exception exception) { if (!_lifetime.IsCancellationRequested) Error = exception.Message; }
        finally { if (!_lifetime.IsCancellationRequested) Busy = false; }
    }

    private void Browse() => _ = BrowseAsync();

    private async Task ConfirmAsync()
    {
        if (!CanConfirm)
            return;
        Busy = true;
        Error = string.Empty;
        var source = Source;
        var selected = Entries.Where(entry => entry.Selected).Select(entry => new InstanceLinkSelection(entry.Preview.VersionId!, entry.GameDirectory, entry.Preview.ProfileId)).ToArray();
        try
        {
            var linked = await Task.Run(() => client.Instances.LinkExistingAsync(source, selected, _lifetime.Token), _lifetime.Token);
            await home.RefreshAsync();
            if (linked.Count > 0)
                home.Select(linked[0].Id);
            Busy = false;
            close();
        }
        catch (Exception exception) { if (!_lifetime.IsCancellationRequested) Error = exception.Message; }
        finally { if (!_lifetime.IsCancellationRequested) Busy = false; }
    }

    protected override IDisposable? OnMounted()
    {
        window.SizeChanged += OnSizeChanged;
        return PopupKeyboardClose.Bind(window, close, () => !Busy);
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs args) => Viewport = args.Size;

    protected override void OnDispose()
    {
        window.SizeChanged -= OnSizeChanged;
        _lifetime.Cancel();
        _scan?.Cancel();
        _scan?.Dispose();
        _lifetime.Dispose();
        base.OnDispose();
    }
}
