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

/// <summary>Edits the persisted starting mode without changing existing or scoped conversation permissions.</summary>
internal sealed partial class AiDefaultMode(AiSettingsSection owner) : Control
{
    private readonly LauncherSettingsCard _card = new(Resources.AiDefaultMode, new AiDefaultModeContent(owner), owner.Window, () => Resources.AiDefaultMode);

    private Ui Body => Border(_card);
}

internal sealed partial class AiDefaultModeContent(AiSettingsSection owner) : Control
{
    private bool Enabled => owner.State.Loaded && !owner.State.Loading && !owner.State.Busy;
    private partial float AvailableWidth { get; set; }

    private void Resize(object? sender, WindowSizeChangedEventArgs args) => AvailableWidth = args.Size.Width - 188f;

    protected override IDisposable? OnMounted()
    {
        AvailableWidth = owner.Window.ClientSize.Width - 188f;
        owner.Window.SizeChanged += Resize;
        return null;
    }

    protected override void OnDispose()
    {
        owner.Window.SizeChanged -= Resize;
        base.OnDispose();
    }

    private int Selected => owner.State.Defaults.Operate ? 0 : 1;

    private Ui Body => new SettingsChoiceRow(
LauncherAppearance.Capture(), Resources.AiDefaultMode,
        [Resources.AiAllowAllMode, Resources.AiSafeMode],
        Selected,
        index =>
        {
            if (index >= 0 && index != Selected)
                _ = owner.State.DefaultModeAsync(index == 0);
        },
        AvailableWidth,
        LauncherTheme.Ink,
        LauncherTheme.Muted,
        LauncherMenus.Choice,
        LauncherMenus.ChoiceFace,
        LauncherMenus.ChoiceMenu,
        "settings.ai.default-mode",
        Resources.AiDefaultModeHelp,
        Enabled);
}

internal sealed partial class AiDefaultAccess(AiSettingsSection owner, bool network) : Control
{
    private bool Value => network ? owner.State.Defaults.Network : owner.State.Defaults.Files;
    private string Title => network ? Resources.AiDefaultNetwork : Resources.AiDefaultFiles;

    private Ui Body => Border(Grid(
            [GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                Column([
                    Text(Title)
                        .FontSize(14f).Foreground(LauncherTheme.Ink)
                        .Wrap(),
                    Text(network ? Resources.SettingsAiNetworkHelp : Resources.SettingsAiFilesHelp)
                        .FontSize(12f).Foreground(LauncherTheme.Muted)
                        .Wrap(),
                ])
                    .Spacing(6f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                ToggleSwitch(Value, value => _ = owner.State.DefaultsAsync(network, value))
                    .Enabled(owner.State.Loaded && !owner.State.Loading && !owner.State.Busy)
                    .NoStatusText()
                    .Palette(LauncherStyles.ContentToggle)
                    .OnSurfacePaint(LauncherToggleSurface.Paint)
                    .Automation(new()
                    {
                        Name = Title,
                        AutomationId = network ? "settings.ai.capabilities" : "ai.defaults.files"
                    })
                    .Cell(1, 0),
            ])
            .ColumnSpacing(16f));
}