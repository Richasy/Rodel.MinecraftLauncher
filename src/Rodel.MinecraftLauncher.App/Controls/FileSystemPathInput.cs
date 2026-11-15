// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Controls;

internal readonly record struct FileSystemPathSettings(
    Window Window,
    string Value,
    Action<string> Change,
    string Label,
    string Id,
    bool Enabled = true,
    bool Folder = false,
    string[]? Extensions = null,
    string? SaveName = null,
    bool Clearable = false,
    string? BrowseId = null,
    string? Placeholder = null);

/// <summary>Displays a selectable read-only path beside its owner-bound system picker.</summary>
internal sealed partial class FileSystemPathInput : Control
{
    private PopupFocusRecovery? _focus;
    private bool _disposed;

    internal FileSystemPathInput(
        Window window,
        string value,
        Action<string> change,
        string label,
        string id,
        bool Enabled = true,
        bool Folder = false,
        string[]? Extensions = null,
        string? SaveName = null,
        bool Clearable = false,
        string? BrowseId = null,
        string? Placeholder = null)
    {
        Input = new(
            window,
            value,
            change,
            label,
            id,
            Enabled,
            Folder,
            Extensions,
            SaveName,
            Clearable,
            BrowseId,
            Placeholder);
        Error = string.Empty;
    }

    private partial FileSystemPathSettings Input { get; set; }
    private partial bool Picking { get; set; }
    private partial string Error { get; set; }
    private string BrowseName => string.Format(Resources.BrowsePath, Input.Label);
    private string PlaceholderText => Input.Placeholder ?? (Input.Folder
        ? Resources.PathFolderPlaceholder
        : Input.SaveName is not null ? Resources.PathSavePlaceholder : Resources.PathFilePlaceholder);

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is FileSystemPathInput other)
            Input = other.Input;
    }

    private Ui Body => Column([
        Grid(
            [GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                TextBox()
                    .Text(Input.Value)
                    .ReadOnly()
                    .Placeholder(PlaceholderText)
                    .Style(LauncherStyles.Input)
                    .MinHeight(40f)
                    .FlowDirection(FlowDirection.LeftToRight)
                    .Automation(new()
                    {
                        Name = Input.Label,
                        AutomationId = Input.Id,
                        HelpText = Input.Value
                    })
                    .Cell(0, 0),
                Row([
                    LauncherControls.OutlineCommand(
                        Row([
                            LauncherIcons.Line("folder", 18f)
                        ]),
                        () => _ = BrowseAsync())
                        .Style(LauncherButtons.BrowseButton)
                        .Width(40f).Height(40f)
                        .Enabled(Input.Enabled && !Picking)
                        .ToolTip(BrowseName)
                        .Automation(new()
                        {
                            Name = BrowseName,
                            AutomationId = Input.BrowseId ?? (Input.Id + ".browse")
                        }),
                    LauncherControls.OutlineCommand(
                        Row([
                            LauncherIcons.Line("close", 16f)
                        ]),
                        () =>
                        {
                            Error = string.Empty;
                            Input.Change(string.Empty);
                        })
                        .Style(LauncherButtons.BrowseButton)
                        .Width(40f).Height(40f)
                        .Enabled(Input.Enabled && !Picking)
                        .Visible(Input.Clearable && Input.Value.Length > 0)
                        .ToolTip(Resources.PathClear)
                        .Automation(new()
                        {
                            Name = Resources.PathClear,
                            AutomationId = Input.Id + ".clear"
                        }),
                ])
                    .Spacing(8f)
                    .Cell(1, 0),
            ])
            .ColumnSpacing(8f),
        Text(Error)
            .FontSize(12f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(Error.Length > 0)
            .Automation(new()
            {
                Name = Error,
                AutomationId = Input.Id + ".error"
            }),
    ])
        .Spacing(8f);

    private async Task BrowseAsync()
    {
        if (!Input.Enabled || Picking || _disposed)
            return;
        var input = Input;
        var returnFocus = input.Window.FocusedElement;
        Picking = true;
        Error = string.Empty;
        try
        {
            string? path;
            if (input.Folder)
            {
                path = (await input.Window.CreateFolderPicker().PickSingleFolderAsync())?.Path;
            }
            else if (input.SaveName is { } name)
            {
                var picker = input.Window.CreateFileSavePicker();
                picker.SuggestedFileName = name;
                picker.FileTypeChoices.Add(input.Label, input.Extensions ?? [".json"]);
                path = (await picker.PickSaveFileAsync())?.Path;
            }
            else
            {
                var picker = input.Window.CreateFileOpenPicker();
                foreach (var extension in input.Extensions ?? ["*"])
                    picker.FileTypeFilter.Add(extension);
                path = (await picker.PickSingleFileAsync())?.Path;
            }

            if (!_disposed && path is not null)
                input.Change(path);
        }
        catch (Exception error)
        {
            if (!_disposed)
                Error = error.Message;
        }
        finally
        {
            if (!_disposed)
            {
                Picking = false;
                _focus?.Request(this, returnFocus);
            }
        }
    }

    protected override IDisposable? OnMounted()
    {
        _focus = new(Input.Window);
        return null;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        _focus?.Dispose();
        base.OnDispose();
    }
}