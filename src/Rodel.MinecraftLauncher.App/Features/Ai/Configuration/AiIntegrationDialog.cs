// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Layout;
using Sprout.Reconcile;
using System.Text.Json;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Previews Core's exact external-client plan and applies only its reviewed fingerprint.</summary>
internal sealed partial class AiIntegrationDialog : Control
{
    private readonly AiConfigurationStore _state;
    private readonly Window _window;
    private readonly string _client;
    private readonly string _clientTitle;
    private readonly Action _close;
    private readonly AiIntegrationFields _fields;
    private AiIntegrationPlan? _plan;
    private bool _disposed;

    internal AiIntegrationDialog(
        AiConfigurationStore state,
        Window window,
        string client,
        string clientTitle,
        Action close)
    {
        _state = state;
        _window = window;
        _client = client;
        _clientTitle = clientTitle;
        _close = close;
        _fields = new(this, window);
        SkillsOnly = true;
        CliPath = string.Empty;
        Preview = string.Empty;
        Error = string.Empty;
    }

    private partial bool Reinstall { get; set; }
    internal partial string CliPath { get; private set; }
    internal partial bool SkillsOnly { get; private set; }
    internal partial bool Working { get; private set; }
    internal partial bool Ready { get; private set; }
    internal partial string Preview { get; private set; }
    internal partial string Error { get; private set; }
    private AiClientIntegrationRequest Request => new()
    {
        Client = _client,
        SkillsOnly = SkillsOnly,
        LauncherExecutable = CliPath.Length == 0 ? null : CliPath
    };

    internal void SetCliPath(string path)
    {
        CliPath = path;
        _ = LoadAsync();
    }

    internal void SetMcp(bool include)
    {
        if (!Working)
        {
            SkillsOnly = !include;
            _ = LoadAsync();
        }
    }

    private static AiDialogAppearance Appearance => AiDialogAppearance.Capture();

    private Ui Body => new AiDialogFrame(
LauncherAppearance.Capture(), Appearance,
        Resources.AiIntegrationPreview,
        _fields,
        _close,
        Reinstall ? Resources.AiReinstall : Resources.AiInstall,
        () => _ = ApplyAsync(),
        Ready && !Working,
        Math.Min(620f, _window.ClientSize.Height - 64f));

    internal async Task LoadAsync()
    {
        if (Working)
            return;
        Working = true;
        Ready = false;
        Error = string.Empty;
        try
        {
            var request = Request;
            var plan = await Task.Run(() => _state.Service.Integrations.PlanAsync(request));
            if (_disposed)
                return;
            _plan = plan;
            if (CliPath.Length == 0 && plan.LauncherExecutable.Length > 0)
                CliPath = plan.LauncherExecutable;
            Preview = JsonSerializer.Serialize(_plan, AiJsonContext.Default.AiIntegrationPlan);
            Reinstall = _plan.McpCurrentFingerprint is not null || _plan.Files.Any(file => !file.Bridge && file.CurrentSha256 is not null);
            Ready = _plan.Fingerprint is not null && _plan.InspectionError is null && _plan.McpAction != "cli_required";
        }
        catch (Exception error)
        {
            if (!_disposed)
                Error = error.Message;
        }
        finally
        {
            if (!_disposed)
                Working = false;
        }
    }

    private async Task ApplyAsync()
    {
        if (!Ready || Working || _plan is null)
            return;
        Working = true;
        Ready = false;
        try
        {
            var result = await _state.Service.Integrations.ApplyAsync(Request with { PlanFingerprint = _plan.Fingerprint });
            if (_disposed)
                return;
            if (result.Status != "completed" || (!SkillsOnly && !result.Mcp.Registered))
            {
                Preview = JsonSerializer.Serialize(result, AiJsonContext.Default.AiIntegrationReceipt);
                Error = Resources.AiIntegrationIncomplete;
                return;
            }

            _close();
            LauncherToast.Show(_window, string.Format(Resources.AiIntegrationInstalled, _clientTitle));
        }
        catch (Exception error)
        {
            if (!_disposed)
                Error = error.Message;
        }
        finally
        {
            if (!_disposed)
                Working = false;
        }
    }

    protected override IDisposable? OnMounted()
    {
        _ = LoadAsync();
        return null;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        base.OnDispose();
    }
}

internal sealed partial class AiIntegrationFields(AiIntegrationDialog owner, Window window) : Control
{
    private Ui Body => Column([
        Text(Resources.AiIntegrationPlan)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap(),
        Row([
            LauncherIcons.Line("check", 18f, LauncherTheme.PrimaryBackground),
            Text(Resources.AiIntegrationSkills)
                .FontSize(14f).Foreground(LauncherTheme.Ink)
                .Automation(new()
                {
                    Name = Resources.AiIntegrationSkills,
                    AutomationId = "ai.integration.skills"
                }),
            Text(Resources.AiRequired)
                .FontSize(11f).Foreground(LauncherTheme.Muted),
        ])
            .Spacing(12f),
        new LauncherCheckBox(
LauncherAppearance.Capture(),            Resources.AiIntegrationMcp,
            !owner.SkillsOnly,
            () => owner.SetMcp(owner.SkillsOnly),
            !owner.Working,
            "ai.integration.mcp"),
        Text(Resources.AiIntegrationCliDescription)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(!owner.SkillsOnly),
        new FileSystemPathInput(
            window,
            owner.CliPath,
            value => owner.SetCliPath(value),
            Resources.AiIntegrationCli,
            "ai.integration.cli",
            Enabled: !owner.Working,
            Extensions: [".exe"]).Visible(!owner.SkillsOnly),
        Text(owner.Error)
            .Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(owner.Error.Length > 0),
        new AiJsonPreview(LauncherAppearance.Capture(),owner.Preview, "ai.integration.plan"),
        LauncherControls.OutlineCommand(Resources.AiReload, () => _ = owner.LoadAsync())
            .Style(LauncherButtons.AiTextAction)
            .Enabled(!owner.Working),
    ])
        .Spacing(14f);
}