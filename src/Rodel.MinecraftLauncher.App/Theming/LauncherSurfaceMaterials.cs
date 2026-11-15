// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Theming;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Theming;

/// <summary>Cached immutable surfaces selected by a retained launcher appearance.</summary>
/// <param name="Theme">Immutable appearance captured by the owning reactive control.</param>
internal readonly record struct LauncherSurfaceMaterials(LauncherAppearance Theme)
{
    private static readonly Brush DarkAcrylic = AcrylicFor(Color.FromHex("#182225"), .65f, .45f);
    private static readonly Brush LightAcrylic = AcrylicFor(Color.FromHex("#FFFDF4"), .68f, .75f);
    internal Brush Acrylic => Theme.HighContrast ? Theme.Background : Theme.Light ? LightAcrylic : DarkAcrylic;

    private static Brush AcrylicFor(Color color, float opacity, float luminosity) => Brush.CreateAcrylic(new AcrylicMaterial
    {
        TintColor = color,
        TintOpacity = opacity,
        TintLuminosityOpacity = luminosity,
        BlurRadius = 20f,
        FallbackColor = color,
    });
    internal DropShadow Shadow => new(Theme.HighContrast ? Colors.Transparent : Theme.Light ? Color.FromHex("#41694C1F") : Color.FromHex("#6E040C0F"), 2f, new(0f, 3f));
    internal Brush Shade => Theme.HighContrast ? Colors.Transparent : Theme.Light ? SceneShade.LightShade : SceneShade.DarkShade;
    internal Brush HorizontalShade => Theme.HighContrast ? Colors.Transparent : Theme.Light ? SceneShade.LightHorizontal : SceneShade.DarkHorizontal;

    private static readonly Brush DarkFace = Brush.CreateLinearRelative(new(0f, 0f), new(0f, 1f), [new(0f, Color.FromHex("#16D5E1D3")), new(.42f, Colors.Black.MultiplyAlpha(0f)), new(1f, Color.FromHex("#26020C11")),]);
    private static readonly Brush LightFace = Brush.CreateLinearRelative(new(0f, 0f), new(0f, 1f), [new(0f, Color.FromHex("#46FFFFFF")), new(.5f, Colors.Black.MultiplyAlpha(0f)), new(1f, Color.FromHex("#09A37734")),]);
    internal Brush FaceGradient => Theme.Light ? LightFace : DarkFace;

    private static readonly Brush LightInsetGradient = InsetGradientFor(LauncherTheme.LightInsetShadow);
    private static readonly Brush DarkInsetGradient = InsetGradientFor(LauncherTheme.DarkInsetShadow);
    internal Brush InsetGradient => Theme.Light ? LightInsetGradient : DarkInsetGradient;

    private static readonly Brush DarkSelectionInsetGradient = InsetGradientFor(LauncherTheme.DarkSelectionInsetShadow);
    internal Brush SelectionInsetGradient => Theme.Light ? LightInsetGradient : DarkSelectionInsetGradient;

    private static Brush InsetGradientFor(Color shadow) => Brush.CreateLinearRelative(new(0f, 0f), new(0f, 1f), [new(0f, shadow.MultiplyAlpha(.4f)), new(.18f, shadow.MultiplyAlpha(.1f)), new(1f, Colors.Black.MultiplyAlpha(0f)),]);
    internal Controls.Buttons.LauncherSegmentedMaterial SegmentedMaterial => new(Theme.ControlBackground, Theme.SelectionBackground, Theme.Edge, FaceGradient, Theme.RaisedLight, Theme.RaisedDark, Theme.InsetDark, Theme.InsetLight, Theme.ShadowColor, Theme.HighContrast);
}