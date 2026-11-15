// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Theming;

/// <summary>Retained scene gradients used by the blurred secondary pages.</summary>
internal static class SceneShade
{
    private static readonly GradientStop[] DarkShadeStops = [new(0f, Color.FromHex("#F5061216")), new(.32f, Color.FromHex("#A508161A")), new(.72f, Color.FromHex("#1E09161B")), new(1f, Color.FromHex("#460B171C")),];
    internal static readonly Brush DarkShade = Brush.CreateLinearRelative(new(0f, 1f), new(0f, 0f), DarkShadeStops);
    private static readonly GradientStop[] LightShadeStops = [new(0f, Color.FromHex("#B3FFF7E1")), new(.28f, Color.FromHex("#42FFF5DD")), new(.65f, Colors.Black.MultiplyAlpha(0f)), new(1f, Colors.Black.MultiplyAlpha(0f)),];
    internal static readonly Brush LightShade = Brush.CreateLinearRelative(new(0f, 1f), new(0f, 0f), LightShadeStops);
    private static readonly GradientStop[] DarkHorizontalStops = [new(0f, Color.FromHex("#6B09161B")), new(.56f, Colors.Black.MultiplyAlpha(0f)), new(1f, Color.FromHex("#26071215")),];
    internal static readonly Brush DarkHorizontal = Brush.CreateLinearRelative(new(0f, 0f), new(1f, 0f), DarkHorizontalStops);
    private static readonly GradientStop[] LightHorizontalStops = [new(0f, Color.FromHex("#3DFFF8E4")), new(.62f, Colors.Black.MultiplyAlpha(0f)), new(1f, Colors.Black.MultiplyAlpha(0f)),];
    internal static readonly Brush LightHorizontal = Brush.CreateLinearRelative(new(0f, 0f), new(1f, 0f), LightHorizontalStops);
}