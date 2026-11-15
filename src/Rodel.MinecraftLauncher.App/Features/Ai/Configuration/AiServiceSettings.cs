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

internal sealed partial class AiProviderGroup : Control
{
    private readonly AiSettingsSection _owner;
    private partial AiUiProvider Provider { get; set; }
    private partial ImageSource? LogoSource { get; set; }

    internal AiProviderGroup(AiSettingsSection owner, AiUiProvider provider, ImageSource? logo)
    {
        _owner = owner;
        SetProvider(provider, logo);
    }

    internal void SetProvider(AiUiProvider provider, ImageSource? logo)
    {
        Provider = provider;
        LogoSource = logo;
    }

    private Ui Body => new AiProviderPresentation(_owner, Provider, LogoSource);
}

[View]
internal readonly partial record struct AiProviderPresentation(AiSettingsSection Owner, AiUiProvider Provider, ImageSource? LogoSource)
{
    /// <summary>Builds the current localized AI presentation using the launcher control roles.</summary>
    public Ui Body => new LauncherSettingsGroup(
        Provider.Name,
        Provider.Endpoint,
        "servers",
        null,
        null,
        new AiProviderCommands(Owner, Provider),
        true,
        Owner.Window,
        artwork: Logo ?? LauncherIcons.Element("servers", AiBrandArtwork.Size),
        automationId: "ai.provider." + Provider.Id,
        items: ItemFactory);
    internal Func<SettingsExpanderItem[]> ItemFactory
    {
        get
        {
            var owner = Owner;
            var provider = Provider;
            return () => [.. provider.Models.Select(model => owner.ModelItem(provider, model))];
        }
    }

    internal IconElement? Logo => LogoSource is { } source ? new ImageIcon(source)
    {
        ShowAsMonochrome = true,
        Size = new Size(AiBrandArtwork.Size, AiBrandArtwork.Size)
    }
        : null;
}

internal sealed partial class AiProviderCommands : Control
{
    private readonly AiSettingsSection _owner;
    private partial AiUiProvider Provider { get; set; }

    internal AiProviderCommands(AiSettingsSection owner, AiUiProvider provider)
    {
        _owner = owner;
        Provider = provider;
    }

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is AiProviderCommands commands)
            Provider = commands.Provider;
    }

    private Ui Body => Row([
        LauncherControls.OutlineCommand(Resources.AiAddServiceModel, () => _owner.OpenModel(Provider))
            .Style(LauncherButtons.OutlineButton.Padding(12f, 4f))
            .MinHeight(32f)
            .Automation(new()
            {
                Name = Resources.AiAddServiceModel,
                AutomationId = "ai.provider.models." + Provider.Id
            }),
        LauncherControls.OutlineCommand(
            Row([
                LauncherIcons.Line("refresh", 16f)
            ]),
            () => _owner.RefreshModels(Provider))
            .Style(LauncherButtons.PopupCloseButton)
            .Width(32f).Height(32f)
            .ToolTip(Resources.AiRefreshModels)
            .Automation(new()
            {
                Name = Resources.AiRefreshModels,
                AutomationId = "ai.provider.refresh." + Provider.Id
            }),
        LauncherControls.OutlineCommand(
            Row([
                LauncherIcons.Line("edit", 16f)
            ]),
            () => _owner.OpenService(Provider))
            .Style(LauncherButtons.PopupCloseButton)
            .Width(32f).Height(32f)
            .ToolTip(Resources.AiEditService)
            .Automation(new() { Name = Resources.AiEditService }),
        LauncherControls.OutlineCommand(
            Row([
                LauncherIcons.Line("delete", 16f, LauncherTheme.Error)
            ]),
            () => _ = _owner.State.RemoveServiceAsync(Provider))
            .Style(LauncherButtons.PopupCloseButton)
            .Width(32f).Height(32f)
            .ToolTip(Resources.AiRemoveService)
            .Automation(new() { Name = Resources.AiRemoveService }),
    ])
        .Spacing(4f);
}