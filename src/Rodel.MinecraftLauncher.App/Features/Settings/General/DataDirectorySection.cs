// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Layout;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed partial class DataDirectorySection : Control
{
    private readonly Window _window;
    private readonly AppSettings _settings;
    private readonly string _activeRoot;

    internal DataDirectorySection(Window window, AppSettings settings, string activeRoot)
    {
        _window = window;
        _settings = settings;
        _activeRoot = activeRoot;
        SelectedRoot = settings.DataDirectory;
        Error = string.Empty;
    }

    /// <summary>Gets the directory persisted for the next application start.</summary>
    public partial string SelectedRoot { get; private set; }
    /// <summary>Gets the current directory selection error.</summary>
    public partial string Error { get; private set; }
    /// <summary>Gets whether a system folder picker is active.</summary>
    public partial bool Picking { get; private set; }
    private string DisplayRoot => string.IsNullOrEmpty(SelectedRoot) ? new Core.Storage.LauncherPaths().Root : SelectedRoot;
    private bool RestartRequired => !string.Equals(System.IO.Path.TrimEndingDirectorySeparator(DisplayRoot), System.IO.Path.TrimEndingDirectorySeparator(_activeRoot), StringComparison.OrdinalIgnoreCase);

    private Ui Body => Column([
        Grid(
            [GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                Column([
                    Text(Resources.DataDirectoryHeading)
                        .FontSize(14f).Foreground(LauncherTheme.Ink),
                    Text(DisplayRoot)
                        .FontSize(12f).Foreground(LauncherTheme.Muted)
                        .Wrap()
                        .FlowDirection(Sprout.Graphics.FlowDirection.LeftToRight)
                        .Automation(new()
                        {
                            Name = DisplayRoot,
                            AutomationId = "settings.data.current",
                            HelpText = Resources.DataDirectoryHelp
                        }),
                ])
                    .Spacing(6f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                Column([
                    Text(Resources.SettingsRestartRequired)
                        .FontSize(12f).Foreground(LauncherTheme.Warning)
                        .Wrap()
                        .Visible(RestartRequired)
                        .Automation(new()
                        {
                            Name = Resources.SettingsRestartRequired,
                            AutomationId = "settings.data.restart",
                            LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                        }),
                    LauncherControls.OutlineCommand(Resources.DataDirectoryChoose, () => _ = ChooseAsync())
                        .MinHeight(40f)
                        .Enabled(!Picking)
                        .Automation(new()
                        {
                            Name = Resources.DataDirectoryChoose,
                            AutomationId = "settings.data.choose"
                        }),
                ])
                    .Spacing(6f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(1, 0),
            ])
            .ColumnSpacing(16f),
        Text(Error)
            .FontSize(12f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(Error.Length > 0),
    ])
        .Spacing(8f);

    private async Task ChooseAsync()
    {
        if (Picking)
            return;
        Picking = true;
        try
        {
            var folder = await _window.CreateFolderPicker().PickSingleFolderAsync();
            if (folder is null)
                return;
            var path = System.IO.Path.GetFullPath(folder.Path);
            await Task.Run(async () =>
                {
                    Directory.CreateDirectory(path);
                    var probe = System.IO.Path.Combine(path, ".rmcl-write-check-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        await File.WriteAllTextAsync(probe, string.Empty).ConfigureAwait(false);
                    }
                    finally
                    {
                        File.Delete(probe);
                    }
                });
            _settings.DataDirectory = path;
            SelectedRoot = path;
            Error = string.Empty;
        }
        catch (Exception)
        {
            Error = Resources.DataDirectoryError;
        }
        finally
        {
            Picking = false;
        }
    }
}