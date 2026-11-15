// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reactive;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Svg;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Three AI settings regions sharing a window-local draft, without a second navigation strip.</summary>
internal sealed partial class AiSettingsSection : Control
{
    internal readonly AiConfigurationStore State;
    internal readonly Window Window;
    private readonly AiDefaultMode _defaultMode;
    private readonly AiLocalModelsGroup _localModels;
    private readonly SettingsRows _access;
    private readonly SettingsRows _external;
    private readonly LauncherSettingsGroup _folders;
    private Flyout? _dialog;

    internal AiSettingsSection(AiConfigurationStore state, Window window)
    {
        State = state;
        Window = window;
        _defaultMode = new(this);
        _localModels = new(this);
        _access = new([new LauncherSettingsCard(Resources.AiDefaultFiles, new AiDefaultAccess(this, false), window, () => Resources.AiDefaultFiles), new LauncherSettingsCard(Resources.AiDefaultNetwork, new AiDefaultAccess(this, true), window, () => Resources.AiDefaultNetwork),]);
        _external = new([
            ExternalCard("codex", "Codex", "settings.ai.integrations"),
            ExternalCard("claude", "Claude Code", "ai.integrations.claude"),
            ExternalCard("copilot", "GitHub Copilot CLI", "ai.integrations.copilot"),
            ExternalCard("deepseek-harness", "DeepSeek Harness", "ai.integrations.deepseek-harness"),
        ]);
        _folders = new(
            Resources.AiFolders,
            Resources.AiFoldersEmpty,
            string.Empty,
            new AiRegistrationList(this, "folder"),
            null,
            new AiFolderCommands(this),
            window: window,
            automationId: "settings.ai.folders",
            localizedHeader: () => Resources.AiFolders,
            localizedDescription: () => State.Folders.Count == 0 ? Resources.AiFoldersEmpty : string.Format(Resources.AiCount, State.Folders.Count));
        UpdateSummaries();
    }

    internal (AiUiProvider Provider, ImageSource? Logo)[] Providers => [.. State.Providers.Where(provider => provider.Protocol != AiProtocols.RodelInference).Select(provider => (provider, State.ProviderLogos.GetValueOrDefault(provider.Id)))];
    internal AiUiProvider[] LocalProviders => [.. State.Providers.Where(provider => provider.Protocol == AiProtocols.RodelInference)];
    private string ErrorText => State.Error;
    internal Func<(AiUiProvider Provider, ImageSource? Logo), AiProviderGroup> ProviderFactory => item => new AiProviderGroup(this, item.Provider, item.Logo);

    private Ui Body => Column([
        Text(ErrorText)
            .FontSize(12f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(ErrorText.Length > 0),
        Text(State.ArtworkWarning)
            .FontSize(11f).Foreground(LauncherTheme.Warning)
            .Wrap()
            .Visible(State.ArtworkWarning.Length > 0),
        Text(Resources.AiConversationDefaults)
            .FontSize(18f).FontWeight(600).Foreground(LauncherTheme.Ink),
        _defaultMode,
        Grid(
            [GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                Text(Resources.AiServices)
                    .FontSize(18f).FontWeight(600).Foreground(LauncherTheme.Ink)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                LauncherControls.Command(Resources.AiAddService, () => OpenService())
                    .Style(LauncherButtons.Button)
                    .Automation(new()
                    {
                        Name = Resources.AiAddService,
                        AutomationId = "settings.ai.services"
                    })
                    .Cell(1, 0),
            ])
            .ColumnSpacing(12f),
        Overlay([
            Border(Text(Resources.AiServicesEmpty)
                    .FontSize(13f).Foreground(LauncherTheme.Muted)
                    .Wrap()
                    .Automation(new() { AutomationId = "settings.ai.services.empty" }))
                .Padding(16f, 20f),
            Rectangle(Colors.Transparent)
                .Stroke(LauncherTheme.Edge, 1f)
                .Dashed(4f, 3f),
        ])
            .Visible(Providers.Length == 0 && LocalProviders.Length == 0),
        _localModels.Visible(LocalProviders.Length > 0),
        ItemsRepeater(
            Providers,
            static item => item.Provider.Id,
            ProviderFactory,
            static (row, item) => row.SetProvider(item.Provider, item.Logo),
            new StackLayout
            {
                Spacing = 12f,
                EstimatedItemExtent = 90f
            }).KeyboardNavigation(singleTabStop: false),
        Text(Resources.AiCapabilities)
            .FontSize(18f).FontWeight(600).Foreground(LauncherTheme.Ink)
            .Margin(new Thickness(0f, 18f, 0f, 0f)),
        _access,
        _folders,
        Text(Resources.AiExternal)
            .FontSize(18f).FontWeight(600).Foreground(LauncherTheme.Ink)
            .Margin(new Thickness(0f, 18f, 0f, 0f)),
        _external,
    ])
        .Spacing(14f)
        .Padding(0f, 0f, 0f, 24f);

    internal Action EditAction(AiUiProvider provider) => () => OpenModel(provider);

    internal void OpenModel(AiUiProvider provider, AiUiModel? model = null)
    {
        if (State.Providers.FirstOrDefault(item => item.Id == provider.Id) is not { } current)
            return;
        Show(close => new AiModelDialog(
                State,
                Window,
                current,
                model,
                close));
    }

    internal void OpenLocalModel()
    {
        var provider = LocalProviders.FirstOrDefault(item => item.Enabled);
        if (provider is not null)
            OpenModel(provider);
        else
            Show(close => new AiServiceDialog(State, Window, close, local: true));
    }

    internal void OpenService(AiUiProvider? provider = null) => Show(close => new AiServiceDialog(State, Window, close, provider));

    private LauncherSettingsCard ExternalCard(string client, string title, string automationId)
        => new(title, new AiExternalRow(this, client, title, automationId), Window);

    internal void OpenIntegration(string client, string title)
        => Show(close => new AiIntegrationDialog(State, Window, client, title, close));

    internal void RefreshModels(AiUiProvider provider) => Show(close => new AiDiscoveryDialog(State, Window, provider, close));

    internal void OpenEntry(string kind, AiUiRegistration? entry = null) => Show(close => new AiRegistrationDialog(
            State,
            Window,
            kind,
            close,
            entry));

    private void Show(Func<Action, Control> factory)
    {
        if (ModalPopup.IsOpen(Window))
            return;
        _dialog = new Flyout(factory)
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false,
            ShouldConstrainToRootBounds = true,
            Transition = PopupTransition.None
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(560f).Padding(0f));
        _dialog.Automation(new()
        {
            AutomationId = "ai.dialog",
            IsDialog = true
        });
        ModalPopup.TryShow(Window, _dialog);
    }
    // The virtualized provider owns its rows. Only model data may outlive that provider's mounted projection.
    internal SettingsExpanderItem ModelItem(AiUiProvider provider, AiUiModel model) => new(new AiModelRow(this, provider, model));

    private void RefreshConfiguration()
    {
        var summary = State.Folders.Count == 0 ? Resources.AiFoldersEmpty : string.Format(Resources.AiCount, State.Folders.Count);
        Untracked.Run(() =>
            {
                _localModels.Refresh();
                _folders.SetSummary(summary);
            });
    }

    private void UpdateSummaries()
    {
        _folders.SetSummary(State.Folders.Count == 0 ? Resources.AiFoldersEmpty : string.Format(Resources.AiCount, State.Folders.Count));
    }

    protected override IDisposable? OnMounted() => new Effect(RefreshConfiguration);

    protected override void OnDispose()
    {
        _dialog?.Hide();
        base.OnDispose();
    }
}

internal sealed partial class AiFolderCommands(AiSettingsSection owner) : Control
{
    private Ui Body => LauncherControls.OutlineCommand(Resources.AiAddFolder, () => owner.OpenEntry("folder"))
        .Style(LauncherButtons.OutlineButton.Padding(12f, 4f))
        .MinHeight(32f)
        .Automation(new()
        {
            Name = Resources.AiAddFolder,
            AutomationId = "ai.folder.add"
        });
}

internal sealed partial class AiBuiltinRow(AiSettingsSection owner, string title, bool separator) : Control
{
    private Ui Body => Border(Grid(
            [GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                LauncherControls.OutlineCommand(
                    Row([
                        Text(title)
                            .FontSize(13f).Foreground(LauncherTheme.Ink)
                    ]),
                    () => owner.OpenEntry("details"))
                    .Style(LauncherButtons.AiTextAction.Padding(0f, 4f))
                    .Automation(new()
                    {
                        Name = title,
                        AutomationId = separator ? "settings.ai.mcp" : "ai.mcp.web"
                    })
                    .Cell(0, 0),
                Text(Resources.AiBuiltin)
                    .FontSize(11f).Foreground(LauncherTheme.Muted)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(1, 0),
            ]))
        .Padding(16f)
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(0f, 0f, 0f, separator ? 1f : 0f);
}

internal sealed partial class AiExternalRow(AiSettingsSection owner, string client, string title, string automationId) : Control
{
    private readonly ImageSource _logo = SvgImageSource.FromFile(System.IO.Path.Combine(
        AppContext.BaseDirectory, "Assets", "AiClients", client + ".svg"));

    private Ui Body => Border(Grid(
        [GridLength.Auto, GridLength.Star(), GridLength.Auto],
        [GridLength.Auto],
        [
            Image(_logo)
                .Stretch(ImageStretch.Uniform)
                .AlphaTint(LauncherTheme.Ink)
                .Width(AiBrandArtwork.Size).Height(AiBrandArtwork.Size)
                .VAlign(VerticalAlignment.Center)
                .Automation(new() { AutomationId = "ai.integrations.logo." + client })
                .Cell(0, 0),
            Column([
                Text(title)
                    .FontSize(14f).Foreground(LauncherTheme.Ink),
                Text(string.Format(Resources.SettingsAiIntegrationHelp, title))
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Wrap(),
            ])
                .Spacing(6f)
                .VAlign(VerticalAlignment.Center)
                .Cell(1, 0),
            LauncherControls.OutlineCommand(Resources.AiInstall, () => owner.OpenIntegration(client, title))
                .Style(LauncherButtons.OutlineButton)
                .Automation(new()
                {
                    Name = Resources.AiInstall + " " + title,
                    AutomationId = automationId
                })
                .Cell(2, 0),
        ])
        .ColumnSpacing(AiBrandArtwork.Gap));
}