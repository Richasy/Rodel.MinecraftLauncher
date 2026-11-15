// Copyright (c) Richasy. All rights reserved.

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

/// <summary>Composes the editor workspace, project state and durable drawing status.</summary>
internal sealed partial class SkinEditorView : Control
{
    private readonly SkinEditorSurface _surface;
    private readonly SkinEditorStartPage _start;
    internal SkinEditorState State { get; } = new();

    internal SkinEditorView(LauncherClient client, Window window, AppSettings settings)
    {
        State.Controller = new(client.Skins, window, State, settings);
        State.Ai = new(client, State);
        State.Palettes = new(State, settings);
        _surface = new(State);
        _start = new(State);
    }

    private Ui Body => When(State.HasProject, new SkinSurfaceHost(_surface), new SkinStartHost(_start));

    protected override IDisposable? OnMounted() => SkinEditorShortcuts.Bind(State);

    protected override void OnDispose()
    {
        State.Ai.Dispose();
        base.OnDispose();
    }
}