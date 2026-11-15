// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Skins;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Loads saved projects for the start page and refreshes when the editor becomes active.</summary>
internal sealed partial class SkinRecentProjects : Control
{
    private readonly ScrollView _scroll;
    internal SkinEditorController Controller { get; }
    internal partial SkinProjectInfo[] Projects { get; set; }
    internal partial string Error { get; set; }
    internal partial bool Loaded { get; set; }
    internal partial DateTimeOffset Now { get; set; }
    private DispatcherQueueTimer? _timer;
    private bool _disposed, _loading, _refresh;

    internal SkinRecentProjects(SkinEditorController controller)
    {
        Controller = controller;
        Projects = [];
        Error = string.Empty;
        _scroll = new(new SkinRecentProjectEntries(this));
    }

    internal SkinProjectInfo? At(int index) => Projects is { } projects && (index < projects.Length) ? projects[index] : null;

    private Ui Body => Border(Grid(
            [GridLength.Star()],
            [GridLength.Auto, GridLength.Star()],
            [
                new SkinStartSectionTitle(LauncherAppearance.Capture(),"history", Resources.SkinRecentProjects, "skin.start.recent.title").Margin(new Thickness(16f, 16f, 16f, 0f))
                    .Cell(0, 0),
                _scroll.Automation(new()
                    {
                        Name = Resources.SkinRecentProjects,
                        AutomationId = "skin.start.recent.scroll"
                    })
                    .Cell(0, 1),
            ])
            .RowSpacing(8f))
        .Background(LauncherTheme.ControlBackground)
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(1f);

    protected override IDisposable? OnMounted()
    {
        Projects = [];
        Error = string.Empty;
        Loaded = false;
        Now = DateTimeOffset.UtcNow;
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(1);
        _timer.Tick += OnTick;
        _timer.Start();
        Controller.Window.Activated += OnActivated;
        _ = LoadAsync();
        return null;
    }

    private void OnTick(object? sender, EventArgs args) => Now = DateTimeOffset.UtcNow;

    private void OnActivated(object? sender, EventArgs args)
    {
        Now = DateTimeOffset.UtcNow;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_disposed)
            return;
        if (_loading)
        {
            _refresh = true;
            return;
        }

        _loading = true;
        try
        {
            do
            {
                _refresh = false;
                try
                {
                    var projects = await Controller.RecentProjectsAsync();
                    if (!_disposed)
                    {
                        Projects = [.. projects.Take(8)];
                        Error = string.Empty;
                    }
                }
                catch (Exception error)
                {
                    if (!_disposed)
                        Error = string.Format(Resources.SkinProjectOpenFailed, error.Message);
                }
                finally
                {
                    if (!_disposed)
                        Loaded = true;
                }
            }
            while (!_disposed && _refresh);
        }
        finally
        {
            _loading = false;
        }
    }

    protected override void OnDispose()
    {
        _disposed = true;
        Controller.Window.Activated -= OnActivated;
        _timer?.Stop();
        _timer?.Tick -= OnTick;
        base.OnDispose();
    }
}

/// <summary>Reads the recent-project state inside its bounded scroll viewport.</summary>
internal sealed partial class SkinRecentProjectEntries(SkinRecentProjects owner) : Control
{
    private Ui Body => LauncherControls.KeyboardGroup(Column([
        Text(Resources.SkinNoRecentProjects)
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .MinHeight(64f)
            .VAlign(VerticalAlignment.Center)
            .Margin(new Thickness(38f, 0f, 12f, 0f))
            .Visible(owner.Loaded && (owner.Projects.Length == 0) && (owner.Error.Length == 0)),
        new SkinRecentProject(LauncherAppearance.Capture(),owner.Controller, owner.At(0), owner.Now),
        new SkinRecentProject(LauncherAppearance.Capture(),owner.Controller, owner.At(1), owner.Now),
        new SkinRecentProject(LauncherAppearance.Capture(),owner.Controller, owner.At(2), owner.Now),
        new SkinRecentProject(LauncherAppearance.Capture(),owner.Controller, owner.At(3), owner.Now),
        new SkinRecentProject(LauncherAppearance.Capture(),owner.Controller, owner.At(4), owner.Now),
        new SkinRecentProject(LauncherAppearance.Capture(),owner.Controller, owner.At(5), owner.Now),
        new SkinRecentProject(LauncherAppearance.Capture(),owner.Controller, owner.At(6), owner.Now),
        new SkinRecentProject(LauncherAppearance.Capture(),owner.Controller, owner.At(7), owner.Now),
        Text(owner.Error)
            .Margin(new Thickness(38f, 0f, 12f, 0f))
            .FontSize(11f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(owner.Error.Length > 0),
    ])
        .Spacing(8f)
        .Padding(new Thickness(16f, 0f, 16f, 16f)));
}