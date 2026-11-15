// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Mounts editor tools and GPU views only while a project is active.</summary>
internal sealed partial class SkinEditorSurface : Control
{
    private readonly ScrollView _inspector;
    private readonly SkinEditorWorkspace _workspace;
    private readonly SkinEditorHistoryPanel _history;
    private readonly SkinEditorAiPanel _ai;
    private SkinEditorState State { get; }

    internal SkinEditorSurface(SkinEditorState state)
    {
        State = state;
        _inspector = new(new SkinEditorInspector(State));
        _workspace = new(State);
        _history = new(State);
        _ai = new(State);
        State.InspectorBounds = () =>
        {
            var bounds = _inspector.Render.Bounds;
            return Rect.FromXywh(bounds.Left + _inspector.Offset.X, bounds.Top + _inspector.Offset.Y, bounds.Width, bounds.Height);
        };
        State.ScrollInspector = delta =>
        {
            var before = _inspector.Offset.Y;
            _inspector.ScrollBy(new(0f, delta), animated: false);
            return _inspector.Offset.Y - before;
        };
    }

    private Ui Body => Border(Grid(
            [GridLength.Star()],
            [GridLength.Star(), GridLength.Auto],
            [
                Overlay([
                    Grid(
                        [GridLength.Pixels(State.RailWidth), GridLength.Star(), GridLength.Pixels(State.InspectorWidth)],
                        [GridLength.Star()],
                        [
                            new SkinEditorToolRail(State).Cell(0, 0),
                            _workspace.Cell(1, 0),
                            Overlay([
                                new LauncherMaterialSurface(LauncherMaterials.Acrylic),
                                Border(Grid(
                                    [GridLength.Star()],
                                    [GridLength.Auto, GridLength.Star()],
                                    [
                                        new SkinEditorTabs(State).Cell(0, 0),
                                        _inspector.Automation(new() { AutomationId = "skin.inspector.scroll" })
                                            .Visible(State.Panel == 0)
                                            .Cell(0, 1),
                                        _ai.Visible(State.Panel == 1)
                                            .Cell(0, 1),
                                        _history.Visible(State.Panel == 2)
                                            .Cell(0, 1),
                                    ]))
                            ])
                                .Cell(2, 0),
                        ]),
                    new Sizer(Orientation.Vertical).Resize(new(State.Width - State.InspectorWidth, State.RailWidth + 320f, State.Width - 240f), new(State.InspectorWidth, 240f, State.InspectorMaximum))
                        .Boundary(State.Width - State.InspectorWidth)
                        .OnResize(args => State.InspectorWidth = (float)args.Neighbor!.Value)
                        .LineColor(LauncherTheme.Edge)
                        .AutoHide(false)
                        .Automation(new()
                        {
                            Name = Resources.SkinSidebarWidth,
                            AutomationId = "skin.sidebar.splitter"
                        }),
                ])
                    .Cell(0, 0),
                new SkinEditorFooter(State).Cell(0, 1),
            ])
            .Background(LauncherTheme.Background))
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(0f, 1f, 0f, 0f);
}

[View]
internal readonly partial record struct SkinSurfaceHost(SkinEditorSurface Surface)
{
    /// <summary>Mounts the editing workspace only in the project branch.</summary>
    public Ui Body => Surface;
}

[View]
internal readonly partial record struct SkinStartHost(SkinEditorStartPage Start)
{
    /// <summary>Mounts the start page only in the empty branch.</summary>
    public Ui Body => Start;
}