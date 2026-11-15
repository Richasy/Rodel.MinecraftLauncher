// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Features.Instances;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Multiplayer;

/// <summary>Uses real save thumbnails and explicit running-session selection in the existing room form.</summary>
internal sealed partial class MultiplayerDraftFields(MultiplayerController controller) : Control
{
    private partial int Revision { get; set; }
    private MultiplayerDraftChoices Choices { get { _ = Revision; return controller.Choices; } }
    private Ui Body => Column([
        Column([
            Text(Resources.MultiplayerGameChoice).FontSize(12f).Foreground(LauncherTheme.Muted),
            LauncherControls.Choice(Choices.GameLabels)
                .SelectedIndex(Choices.Games.ToList().FindIndex(game => game.Id == Choices.SessionId)).OnSelectionChanged(Choices.SelectGame)
                .Enabled(!Choices.Loading).MinHeight(40f)
                .Automation(new() { Name = Resources.MultiplayerGameChoice, AutomationId = "multiplayer.draft.game" }),
        ]).Spacing(10f).Visible(Choices.Games.Count > 0),
        Column([
            Text(Resources.MultiplayerWorldChoice).FontSize(12f).Foreground(LauncherTheme.Muted),
            Grid([GridLength.Auto, GridLength.Star()], [GridLength.Auto], [
                new InstanceIconView(Choices.IconData, 40f, 40f).Visible(Choices.WorldFolder is not null).VAlign(VerticalAlignment.Center).Cell(0, 0),
                LauncherControls.Choice(new[] { Resources.MultiplayerWorldInGame }.Concat(Choices.Worlds.Select(world => world.Name)))
                    .SelectedIndex(Choices.WorldFolder is null ? 0 : Choices.Worlds.ToList().FindIndex(world => world.Folder == Choices.WorldFolder) + 1)
                    .OnSelectionChanged(Choices.SelectWorld).Enabled(!Choices.Loading).MinHeight(40f).Cell(1, 0)
                    .Automation(new() { Name = Resources.MultiplayerWorldChoice, AutomationId = "multiplayer.draft.world" }),
            ]).ColumnSpacing(Choices.WorldFolder is null ? 0f : 12f),
        ]).Spacing(10f).Visible(controller.Role == "host" && Choices.Games.Count == 0),
    ]).Spacing(12f);

    protected override IDisposable? OnMounted() { controller.Changed += OnChanged; return null; }
    private void OnChanged() => Revision++;
    protected override void OnDispose() { controller.Changed -= OnChanged; base.OnDispose(); }
}