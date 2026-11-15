// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Numerics;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Multiplayer;

/// <summary>Offers one of the two room entry paths in the approved, equally divided workspace.</summary>
/// <param name="Host">Whether this entry shares a local world rather than joining a friend's world.</param>
/// <param name="Open">Opens the corresponding persistent room dialog.</param>
/// <param name="Pending">Whether this path has an unfinished preparation draft or task.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct MultiplayerEntry(LauncherAppearance Theme, bool Host, Action Open, bool Pending)
{
    /// <summary>Centers equal role groups and anchors recovery below them without changing their alignment.</summary>
    public Ui Body => Grid([GridLength.Star()], [GridLength.Star(), GridLength.Auto, GridLength.Star()], [
        Column([
            new MultiplayerEntryArtwork(Host, Theme.Muted)
                .HAlign(HorizontalAlignment.Center).Margin(new Thickness(0f, 0f, 0f, 20f)),
            Text(Host ? Resources.MultiplayerCreate : Resources.MultiplayerJoin).FontSize(25f).FontWeight(600).Foreground(Theme.Ink)
                .HAlign(HorizontalAlignment.Center)
                .Automation(new() { AutomationId = Host ? "multiplayer.entry.host.title" : "multiplayer.entry.guest.title" }),
            Text(Host ? Resources.MultiplayerLocalWorld : Resources.MultiplayerFriendWorld).FontSize(13f).Foreground(Theme.Muted)
                .HAlign(HorizontalAlignment.Center).Margin(new Thickness(0f, 10f, 0f, 28f))
                .Automation(new() { AutomationId = Host ? "multiplayer.entry.host.description" : "multiplayer.entry.guest.description" }),
            LauncherControls.OutlineCommand(Theme,Row([
                LauncherIcons.Line("forward", 18f),
                Text(Resources.MultiplayerEnterCode),
            ]).Spacing(8f).HAlign(HorizontalAlignment.Center), Open).Width(180f).Visible(!Host)
                .Automation(new() { Name = Resources.MultiplayerEnterCode, AutomationId = Host ? "multiplayer.join.hidden" : "multiplayer.join" }),
            LauncherControls.Command(Theme,Row([
                LauncherIcons.Line("plus", 18f),
                Text(Resources.MultiplayerChooseInstance),
            ]).Spacing(8f).HAlign(HorizontalAlignment.Center), Open).Style(Theme.Buttons.Accent).Width(180f).Visible(Host)
                .Automation(new() { Name = Resources.MultiplayerChooseInstance, AutomationId = Host ? "multiplayer.create" : "multiplayer.create.hidden" }),
        ]).HAlign(HorizontalAlignment.Center).Cell(0, 1),
        LauncherControls.OutlineCommand(Theme,Row([
            LauncherIcons.Line("download", 16f),
            Text(Resources.PackResumePreparation).FontSize(12f),
        ]).Spacing(8f), Open).Style(Theme.Buttons.AiTextAction).Visible(Pending)
            .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Top).Margin(new Thickness(0f, 12f, 0f, 0f))
            .Automation(new() { Name = Resources.PackResumePreparation, AutomationId = Host ? "multiplayer.resume.host" : "multiplayer.resume.guest" }).Cell(0, 2),
    ]).Margin(new Thickness(24f, 16f, 24f, 16f));
}

/// <summary>Paints the prototype's room artwork with native paths and current theme brushes.</summary>
internal sealed class MultiplayerEntryArtwork(bool host, Brush ink) : Control
{
    private readonly bool _host = host;
    private readonly Brush _ink = ink;
    protected override void OnMount() => SetProjection(new MultiplayerArtworkWidget(_host, _ink), null, null);

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is MultiplayerEntryArtwork artwork && Render is MultiplayerArtworkWidget widget)
        {
            widget.Configure(artwork._host, artwork._ink);
        }
    }
}

internal sealed class MultiplayerArtworkWidget(bool host, Brush ink) : Widget
{
    private bool _host = host;
    private Brush _ink = ink;
    private static readonly PathData World = PathData.Parse("M12 2 L21 7 L12 12 L3 7 Z M3 7 V17 L12 22 L21 17 V7 M12 12 V22");
    private static readonly PathData People = PathData.Parse("M8 9 A3 3 0 1 0 8 3 A3 3 0 0 0 8 9 M2 20 V17 A6 6 0 0 1 14 17 V20 M17 4 A3 3 0 0 1 17 10 M17 14 A5 5 0 0 1 22 19 V20");

    protected override Size MeasureOverride(BoxConstraints constraints, LayoutContext context) => new(240f, 112f);

    /// <summary>Updates the room role while retaining the same native drawing widget.</summary>
    public void Configure(bool host, Brush ink)
    {
        if (_host == host && _ink == ink) return;
        _host = host;
        _ink = ink;
        Invalidate(WidgetDirty.Paint);
    }

    protected override void PaintPresentable(ISceneRenderer renderer)
    {
        var stroke = StrokeStyle.Solid with { LineJoin = PenLineJoin.Round, StartCap = PenLineCap.Round, EndCap = PenLineCap.Round };
        renderer.PushTransform(Matrix3x2.CreateScale(3.5f) * Matrix3x2.CreateTranslation(Bounds.Left + 78f, Bounds.Top + 14f));
        renderer.StrokePath(_host ? World : People, _ink, 1.1f, stroke);
        renderer.PopTransform();
    }
}