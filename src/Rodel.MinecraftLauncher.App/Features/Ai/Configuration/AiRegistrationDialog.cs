// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Edits a configuration registration without borrowing chat state or conversation actions.</summary>
internal sealed partial class AiRegistrationDialog : Control
{
    private readonly AiConfigurationStore _configuration;
    private readonly AiUiRegistration? _existing;
    private readonly Action _close;
    private readonly AiRegistrationFields _fields;
    private bool _disposed;

    internal AiRegistrationDialog(
        AiConfigurationStore configuration,
        Window window,
        string kind,
        Action close,
        AiUiRegistration? existing = null)
    {
        _configuration = configuration;
        Window = window;
        Kind = kind;
        _close = close;
        _existing = existing;
        _fields = new(this);
        Name = existing?.Name ?? string.Empty;
        Path = existing?.Path ?? string.Empty;
        Arguments = existing?.Arguments ?? string.Empty;
        Directory = existing?.WorkingDirectory ?? string.Empty;
        Environment = existing?.Environment ?? string.Empty;
        Credential = existing?.Credential ?? string.Empty;
        Enabled = existing?.Enabled ?? true;
        Access = existing?.Mode == "write" ? 1 : 0;
        Error = string.Empty;
    }

    internal Window Window { get; }
    internal string Kind { get; }
    internal partial string Name { get; set; }
    internal partial string Path { get; set; }
    internal partial string Arguments { get; set; }
    internal partial string Directory { get; set; }
    internal partial string Environment { get; set; }
    internal partial string Credential { get; set; }
    internal partial bool Enabled { get; set; }
    internal partial int Access { get; set; }
    internal partial string Error { get; private set; }
    internal partial bool Working { get; private set; }
    private string Title => _existing is not null ? Resources.AiEdit : Kind switch
    {
        "folder" => Resources.AiAddFolder,
        "skill" => Resources.AiAddSkill,
        "mcp" => Resources.AiAddMcp,
        _ => Resources.AiDetails,
    };
    private bool CanSubmit => !Working && (Kind switch
    {
        "folder" or "skill" => Path.Trim().Length > 0,
        "mcp" => Name.Trim().Length > 0 && Path.Trim().Length > 0,
        _ => false,
    });

    private Ui Body => new AiDialogFrame(
LauncherAppearance.Capture(), AiDialogAppearance.Capture(),
        Title,
        _fields,
        _close,
        Resources.AiSave,
        () => _ = SubmitAsync(),
        CanSubmit,
        Math.Min(Kind == "mcp" ? 560f : 440f, Window.ClientSize.Height - 64f),
        SingleAction: Kind == "details");

    private async Task SubmitAsync()
    {
        if (!CanSubmit)
            return;
        Working = true;
        var saved = await _configuration.SaveEntryAsync(Kind, new(
                _existing?.Id ?? string.Empty,
                Name.Trim(),
                Path.Trim(),
                Enabled,
                Access == 0 ? "read" : "write",
                Arguments,
                Directory,
                Environment,
                Credential), _existing is not null);
        if (_disposed)
            return;
        Error = _configuration.Error;
        Working = false;
        if (saved)
            _close();
    }

    protected override void OnDispose()
    {
        _disposed = true;
        base.OnDispose();
    }
}

internal sealed partial class AiRegistrationFields(AiRegistrationDialog owner) : Control
{
    private Ui Body => Column([
        Text(owner.Error)
            .FontSize(12f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(owner.Error.Length > 0),
        Text(owner.Kind == "folder"
                ? Resources.AiFolderNotice
                : owner.Kind == "skill" ? Resources.AiSkillIntro : Resources.AiExtensionsHelp)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(owner.Kind is "folder" or "skill" or "mcp"),
        Text(Resources.AiDisplayName)
            .FontSize(13f).Foreground(LauncherTheme.Ink)
            .Visible(owner.Kind == "mcp"),
        TextBox(owner.Name, value => owner.Name = value)
            .Style(LauncherStyles.Input)
            .Visible(owner.Kind == "mcp")
            .Automation(new()
            {
                Name = Resources.AiDisplayName,
                AutomationId = "ai.entry.name"
            }),
        Text(owner.Kind == "mcp" ? Resources.AiMcpExecutable : Resources.AiFolderPath)
            .FontSize(13f).Foreground(LauncherTheme.Ink)
            .Visible(owner.Kind is "folder" or "skill" or "mcp"),
        new FileSystemPathInput(
            owner.Window,
            owner.Path,
            value => owner.Path = value,
            owner.Kind == "mcp" ? Resources.AiMcpExecutable : Resources.AiFolderPath,
            "ai.entry.path",
            Enabled: !owner.Working,
            Folder: owner.Kind != "mcp").Visible(owner.Kind is "folder" or "skill" or "mcp"),
        Text(Resources.AiFolderAccess)
            .FontSize(13f).Foreground(LauncherTheme.Ink)
            .Visible(owner.Kind == "folder"),
        LauncherControls.Choice([Resources.AiReadOnly, Resources.AiFolderReadWrite]).MinHeight(40f)
            .SelectedIndex(owner.Access)
            .OnSelectionChanged(value => owner.Access = value)
            .Visible(owner.Kind == "folder")
            .Automation(new()
            {
                Name = Resources.AiFolderAccess,
                AutomationId = "ai.entry.access"
            }),
        Column([
            Text(Resources.AiMcpArguments)
                .FontSize(13f).Foreground(LauncherTheme.Ink),
            TextBox(owner.Arguments, value => owner.Arguments = value)
                .AcceptsReturn()
                .Height(80f)
                .Style(LauncherStyles.Input)
                .Automation(new()
                {
                    Name = Resources.AiMcpArguments,
                    AutomationId = "ai.entry.arguments"
                }),
            Text(Resources.AiMcpDirectory)
                .FontSize(13f).Foreground(LauncherTheme.Ink),
            new FileSystemPathInput(
                owner.Window,
                owner.Directory,
                value => owner.Directory = value,
                Resources.AiMcpDirectory,
                "ai.entry.directory",
                Enabled: !owner.Working,
                Folder: true,
                Clearable: true),
            Text(Resources.AiMcpEnvironment)
                .FontSize(13f).Foreground(LauncherTheme.Ink),
            TextBox(owner.Environment, value => owner.Environment = value)
                .AcceptsReturn()
                .Height(80f)
                .Style(LauncherStyles.Input),
            Text(Resources.AiMcpCredential)
                .FontSize(13f).Foreground(LauncherTheme.Ink),
            TextBox(owner.Credential, value => owner.Credential = value)
                .Style(LauncherStyles.Input),
        ])
            .Spacing(10f)
            .Visible(owner.Kind == "mcp"),
        Grid(
            [GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                Text(Resources.AiMcpEnable)
                    .FontSize(13f).Foreground(LauncherTheme.Ink)
                    .Wrap()
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                ToggleSwitch(owner.Enabled, value => owner.Enabled = value)
                    .NoStatusText()
                    .Palette(LauncherStyles.ContentToggle)
                    .OnSurfacePaint(LauncherToggleSurface.Paint)
                    .Automation(new()
                    {
                        Name = Resources.AiMcpEnable,
                        AutomationId = "ai.entry.enabled"
                    })
                    .Cell(1, 0),
            ])
            .ColumnSpacing(16f)
            .Visible(owner.Kind is "skill" or "mcp"),
        Text(Resources.AiMcpTrust)
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(owner.Kind == "mcp"),
        Text(Resources.AiMcpIntro)
            .FontSize(13f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(owner.Kind == "details"),
    ])
        .Spacing(12f);
}