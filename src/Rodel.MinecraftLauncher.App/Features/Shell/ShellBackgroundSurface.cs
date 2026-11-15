// Copyright (c) Richasy. All rights reserved.

using Sprout;

namespace Rodel.MinecraftLauncher.App.Features.Shell;

/// <summary>Retains the complete wallpaper treatment on its own compositor surface until the scene changes.</summary>
internal sealed partial class ShellBackgroundSurface : Control
{
    internal ShellBackgroundSurface(ShellBackgroundView scene) => Scene = scene;

    /// <summary>Gets or sets the wallpaper, viewport, page treatment and theme recorded on the retained surface.</summary>
    public partial ShellBackgroundView Scene { get; set; }

    private ShellBackgroundView Body => Scene;

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) => Scene = ((ShellBackgroundSurface)source).Scene;
}