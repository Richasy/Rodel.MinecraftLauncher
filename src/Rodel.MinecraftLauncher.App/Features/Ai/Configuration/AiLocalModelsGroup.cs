// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Reconcile;
using Sprout.Styling;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Collects launcher-owned GGUF bindings in one native group while retaining each binding's service identity.</summary>
internal sealed partial class AiLocalModelsGroup : Control
{
    private readonly AiSettingsSection _owner;
    private readonly LauncherSettingsGroup _group;

    internal AiLocalModelsGroup(AiSettingsSection owner)
    {
        _owner = owner;
        _group = new(
            Resources.AiLocalKind,
            string.Empty,
            string.Empty,
            null,
            null,
            new AiLocalModelCommands(owner),
            true,
            owner.Window,
            "settings.ai.local-models",
            artwork: LauncherIcons.Element("servers", AiBrandArtwork.Size),
            localizedHeader: () => Resources.AiLocalKind,
            items: Items);
    }

    private SettingsExpanderItem[] Items() => [.. _owner.LocalProviders.SelectMany(provider => provider.Models.Select(model => _owner.ModelItem(provider, model)))];

    internal void Refresh() => _group.RefreshItems();

    private Ui Body => _group;
}

internal sealed partial class AiLocalModelCommands(AiSettingsSection owner) : Control
{
    private Ui Body => LauncherControls.OutlineCommand(Resources.AiAddServiceModel, () => owner.OpenLocalModel())
        .Style(LauncherButtons.OutlineButton.Padding(12f, 4f))
        .MinHeight(32f)
        .Automation(new()
        {
            Name = Resources.AiAddServiceModel,
            AutomationId = "ai.local.models.add"
        });
}