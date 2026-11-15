// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Controls;

/// <summary>Retains a panel's native backdrop separately from its sharp interactive foreground.</summary>
internal sealed partial class LauncherMaterialSurface : Control
{
    private readonly CompositionAcrylicSurface _acrylic = new(new AcrylicMaterial());

    internal LauncherMaterialSurface(Brush material) => Material = material;

    /// <summary>Gets or sets the current theme's panel brush, including its opaque high contrast fallback.</summary>
    public partial Brush Material { get; set; }

    private Ui Body => Overlay([
        _acrylic.Material(Material.AsAcrylic ?? default).Visible(Material.AsAcrylic is not null),
        Rectangle(Material).Visible(Material.AsAcrylic is null),
    ]);

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) => Material = ((LauncherMaterialSurface)source).Material;
}