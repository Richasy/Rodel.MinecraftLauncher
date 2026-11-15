// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Widgets;
using System.Numerics;

namespace Rodel.MinecraftLauncher.App.Controls.Graphics;

/// <summary>Continuous vector paths from the approved HTML, in their original 24-unit canvas.</summary>
internal static class LauncherIcons
{
    /// <summary>The shared spawn flag and landing marker used by the map and its recenter command.</summary>
    internal static readonly PathData WorldSpawnGeometry = PathData.Parse("M5 22 V3 M5 4 Q10 1 14 4 Q18 7 22 4 V14 Q18 17 14 14 Q10 11 5 14 M2 22 H10");
    /// <summary>The compass geometry shared by the world-origin pin and its recenter command.</summary>
    internal static readonly PathData WorldOriginGeometry = PathData.Parse(
        "M12 2 L22 12 L12 22 L2 12 Z M12 2 V7 M12 17 V22 M2 12 H7 M17 12 H22 " +
        "M12 9 A3 3 0 1 0 12 15 A3 3 0 1 0 12 9");
    /// <summary>The directional player marker; native yaw is applied by a retained render transform.</summary>
    internal static readonly PathData WorldPlayerGeometry = PathData.Parse("M12 2 L21 21 L12 16 L3 21 Z");

    private static readonly Dictionary<string, PathData> Paths = new()
    {
        ["world-origin"] = WorldOriginGeometry,
        ["multiplayer"] = PathData.Parse("M8 8 A3 3 0 1 0 2 8 A3 3 0 0 0 8 8 M22 8 A3 3 0 1 0 16 8 A3 3 0 0 0 22 8 M1 21 V18 Q1 14 5 14 Q9 14 9 18 V21 M15 21 V18 Q15 14 19 14 Q23 14 23 18 V21 M9 10 H15 M12 7 V13"),
        ["person"] = PathData.Parse("M16 7 A4 4 0 1 0 8 7 A4 4 0 0 0 16 7 M4 22 V19 A8 8 0 0 1 20 19 V22"),
        ["player"] = PathData.Parse("M15.5 6.5 A3.5 3.5 0 1 0 8.5 6.5 A3.5 3.5 0 1 0 15.5 6.5 M5 21 V18 A7 7 0 0 1 19 18 V21"),
        ["player-follow"] = PathData.Parse("M12 2 V6 M12 18 V22 M2 12 H6 M18 12 H22 M18 12 A6 6 0 1 0 6 12 A6 6 0 1 0 18 12"),
        ["world-spawn"] = WorldSpawnGeometry,
        ["map-point"] = PathData.Parse("M12 2 V6 M12 18 V22 M2 12 H6 M18 12 H22 M16 12 A4 4 0 1 0 8 12 A4 4 0 1 0 16 12"),
        ["map-layers"] = PathData.Parse("M2 8 L12 3 L22 8 L12 13 Z M2 12 L12 17 L22 12 M2 16 L12 21 L22 16"),
        ["seed-generate"] = PathData.Parse("M3 5 L9 3 L15 5 L21 3 V10 M3 5 V21 L9 19 L12 20 M9 3 V19 M15 5 V10 M14 17 H22 M18 13 V21"),
        ["world-options"] = PathData.Parse("M2 14 L8 5 L13 12 L17 7 L22 14 M3 19 H21 M8 17 V21"),
        ["cut"] = PathData.Parse("M9 8 L21 20 M9 16 L21 4 M9 6 A3 3 0 1 0 3 6 A3 3 0 0 0 9 6 M9 18 A3 3 0 1 0 3 18 A3 3 0 0 0 9 18"),
        ["copy"] = PathData.Parse("M8 8 H21 V21 H8 Z M16 8 V3 H3 V16 H8"),
        ["external-link"] = PathData.Parse("M7 17 L17 7 M7 7 H17 V17"),
        ["store"] = PathData.Parse("M3 7 H21 V21 H3 Z M8 7 V5 A4 4 0 0 1 16 5 V7 M8 11 V13 M16 11 V13"),
        ["code"] = PathData.Parse("M8 6 L2 12 L8 18 M16 6 L22 12 L16 18 M14 3 L10 21"),
        ["chat"] = PathData.Parse("M3 3 H21 V17 H9 L3 22 Z M7 8 H17 M7 12 H13"),
        ["image"] = PathData.Parse("M3 4 H21 V20 H3 Z M3 16 L8 11 L13 16 L16 13 L21 18 M17 8 A1.5 1.5 0 1 0 14 8 A1.5 1.5 0 1 0 17 8"),
        ["palette-new"] = PathData.Parse("M3 3 H10 V10 H3 Z M14 3 H21 V10 H14 Z M3 14 H10 V21 H3 Z M17.5 13 V22 M13 17.5 H22"),
        ["image-picker"] = PathData.Parse("M21 9 V4 H3 V20 H9 M3 15 L8 10 L12 14 M17.5 8 A1.5 1.5 0 1 0 14.5 8 A1.5 1.5 0 1 0 17.5 8 M18 10 L23 15 M20 12 L12 20 L11 23 L14 22 L22 14 M15 17 L18 20"),
        ["chat-add"] = PathData.Parse("M21 12 V4 H3 V17 H7 V21 L12 17 H13 M18 14 V22 M14 18 H22"),
        ["clipboard"] = PathData.Parse("M9 4 H5 V22 H21 V4 H17 M9 2 H17 V7 H9 Z M9 12 H17 M9 17 H15"),
        ["history"] = PathData.Parse("M3 10 A9 9 0 1 1 5 19 M3 4 V10 H9 M12 7 V12 L16 15"),
        ["star"] = PathData.Parse("M12 2 L15 8.5 L22 9.5 L17 14.5 L18.5 22 L12 18.5 L5.5 22 L7 14.5 L2 9.5 L9 8.5 Z"),
        ["zoom-in"] = PathData.Parse("M5 12 H19 M12 5 V19"),
        ["zoom-out"] = PathData.Parse("M5 12 H19"),
        ["skin-undo"] = PathData.Parse("M9 5 L3 11 L9 17 M3 11 H14 A6 6 0 0 1 14 23"),
        ["skin-redo"] = PathData.Parse("M15 5 L21 11 L15 17 M21 11 H10 A6 6 0 0 0 10 23"),
        ["skin-grid"] = PathData.Parse("M3 3 H21 V21 H3 Z M9 3 V21 M15 3 V21 M3 9 H21 M3 15 H21"),
        ["skin-uv"] = PathData.Parse("M3 3 H21 V21 H3 Z M12 3 V21 M12 12 H21"),
        ["skin-brush"] = PathData.Parse("M14 3 L21 10 L10 21 H3 V14 Z M11 6 L18 13 M3 14 L10 21"),
        ["skin-eraser"] = PathData.Parse("M13 3 L22 12 L12 22 H7 L2 17 Z M7 12 L16 21 M12 22 H22"),
        ["skin-fill"] = PathData.Parse("M9 3 L18 12 L9 21 L1 13 Z M3 11 H17 M9 3 V1 M20 14 Q16 20 20 22 Q24 20 20 14"),
        ["skin-picker"] = PathData.Parse("M14 3 L21 10 M16 5 L4 17 L3 22 L8 21 L20 9 M10 11 L14 15"),
        ["skin-mirror"] = PathData.Parse("M12 2 V22 M8 6 L2 12 L8 18 Z M16 6 L22 12 L16 18 Z"),
        ["layer-drag"] = PathData.Parse("M9 5 V6 M15 5 V6 M9 11 V12 M15 11 V12 M9 17 V18 M15 17 V18"),
        ["layer-eye"] = PathData.Parse("M2 12 Q12 1 22 12 Q12 23 2 12 Z M15 12 A3 3 0 1 0 9 12 A3 3 0 1 0 15 12"),
        ["layer-eye-off"] = PathData.Parse("M3 3 L21 21 M2 12 Q5 7 8 6 M12 5 Q18 5 22 12 Q20 16 17 17 M13 19 Q6 19 2 12"),
        ["skin-selection"] = PathData.Parse("M3 7 V3 H7 M11 3 H14 M18 3 H21 V7 M21 11 V14 M21 18 V21 H17 M13 21 H10 M6 21 H3 V17 M3 13 V10"),
        ["skin-add"] = PathData.Parse("M12 4 V20 M4 12 H20"),
        ["skin-import"] = PathData.Parse("M9 3 H21 V21 H9 V17 M9 3 V7 M2 12 H15 M11 8 L15 12 L11 16 M14 18 L17 15 L21 19"),
        ["skin-outfit-add"] = PathData.Parse("M8 3 H5 L1 7 L4 10 L6 8 V20 H13 M8 3 V5 H13 V3 H16 L20 7 L17 10 L15 8 V12 M19 13 V21 M15 17 H23"),
        ["plus"] = PathData.Parse("M12 4 V20 M4 12 H20"),
        ["globe"] = PathData.Parse("M12 3 A9 9 0 1 0 12 21 A9 9 0 0 0 12 3 M3 12 H21 M12 3 C7 8 7 16 12 21 M12 3 C17 8 17 16 12 21"),
        ["send"] = PathData.Parse("M4 12 L12 4 L20 12 M12 4 V21"),
        ["book"] = PathData.Parse("M3 4 H9 L12 6 L15 4 H21 V20 H15 L12 22 L9 20 H3 Z M12 6 V22"),
        ["plug"] = PathData.Parse("M8 3 V8 M16 3 V8 M5 8 H19 V11 A7 7 0 0 1 12 18 V22 M8 8 V11 M16 8 V11"),
        ["edit"] = PathData.Parse("M4 16 L16 4 L20 8 L8 20 H4 Z M13 7 L17 11"),
        ["warning"] = PathData.Parse("M12 3 L22 21 H2 Z M12 9 V14 M12 17 V18"),
        ["error"] = PathData.Parse("M12 3 A9 9 0 1 0 12 21 A9 9 0 0 0 12 3 M9 9 L15 15 M15 9 L9 15"),
        ["home"] = PathData.Parse("M3 11 L12 3 L21 11 M5 10 V21 H19 V10 M9 21 V14 H15 V21"),
        ["instances"] = PathData.Parse("M12 2 L21 7 V17 L12 22 L3 17 V7 Z M3 7 L12 12 L21 7 M12 12 V22"),
        ["content"] = PathData.Parse("M3 3 H10 V10 H3 Z M14 3 H21 V10 H14 Z M3 14 H10 V21 H3 Z M14 14 H21 V21 H14 Z"),
        ["download"] = PathData.Parse("M12 3 V15 M7 10 L12 15 L17 10 M4 16 V21 H20 V16"),
        ["settings"] = PathData.Parse("M12 8 A4 4 0 1 0 12 16 A4 4 0 0 0 12 8 M10 2 H14 L15 5 L18 6 L21 5 L23 9 L21 11 V14 L23 16 L21 20 L18 19 L15 20 L14 23 H10 L9 20 L6 19 L3 20 L1 16 L3 14 V11 L1 9 L3 5 L6 6 L9 5 Z"),
        ["sliders"] = PathData.Parse("M4 6 H20 M4 12 H20 M4 18 H20 M8 3 V9 M16 9 V15 M10 15 V21"),
        ["wrench"] = PathData.Parse("M19 2 L15 6 V9 H18 L22 5 C23 9 20 13 16 12 L8 20 C5 23 1 19 4 16 L12 8 C11 4 15 1 19 2 Z"),
        ["spark"] = PathData.Parse("M12 3 L14.5 9.5 L21 12 L14.5 14.5 L12 21 L9.5 14.5 L3 12 L9.5 9.5 Z"),
        ["play"] = PathData.Parse("M7 4 L21 12 L7 20 Z"),
        ["expand"] = PathData.Parse("M14 4 H20 V10 M20 4 L13 11 M10 20 H4 V14 M4 20 L11 13"),
        ["chevron-down"] = PathData.Parse("M6 9 L12 15 L18 9"),
        ["chevron-up"] = PathData.Parse("M6 15 L12 9 L18 15"),
        ["info"] = PathData.Parse("M12 3 A9 9 0 1 0 12 21 A9 9 0 0 0 12 3 M12 7 V8 M12 11 V17"),
        ["check"] = PathData.Parse("M5 12 L10 17 L19 7"),
        ["success"] = PathData.Parse("M12 3 A9 9 0 1 0 12 21 A9 9 0 0 0 12 3 M7 12 L10.5 15.5 L17 9"),
        ["close"] = PathData.Parse("M5 5 L19 19 M19 5 L5 19"),
        ["minimize"] = PathData.Parse("M5 17 H19"),
        ["delete"] = PathData.Parse("M4 6 H20 M9 6 V3 H15 V6 M6 6 L7 21 H17 L18 6 M10 10 V17 M14 10 V17"),
        ["refresh"] = PathData.Parse("M20 10 A8 8 0 1 0 19 17 M20 4 V10 H14"),
        ["export"] = PathData.Parse("M12 15 V3 M7 8 L12 3 L17 8 M4 13 V21 H20 V13"),
        ["folder"] = PathData.Parse("M3 6 H10 L12 8 H21 V20 H3 Z M3 6 V4 H9 L11 6 H19 V8"),
        ["archive"] = PathData.Parse("M5 3 H15 L19 7 V21 H5 Z M15 3 V7 H19 M10 3 V5 H12 V7 H10 V9 H12 V11 H10 V13 M10 13 H13 V17 H10 Z"),
        ["search"] = PathData.Parse("M17 10 A7 7 0 1 0 3 10 A7 7 0 1 0 17 10 M15 15 L21 21"),
        ["search-empty"] = PathData.Parse("M17 10 A7 7 0 1 0 3 10 A7 7 0 1 0 17 10 M15 15 L21 21 M7 10 H13"),
        ["cloud-off"] = PathData.Parse("M6 17 H5 A4 4 0 0 1 4 9 M8 7 A6 6 0 0 1 19 9 A4 4 0 0 1 19 17 H12 M3 3 L21 21"),
        ["back"] = PathData.Parse("M10 5 L3 12 L10 19 M3 12 H21"),
        ["forward"] = PathData.Parse("M14 5 L21 12 L14 19 M21 12 H3"),
        ["stop"] = PathData.Parse("M5 5 H19 V19 H5 Z"),
        ["computer"] = PathData.Parse("M3 4 H21 V17 H3 Z M12 17 V21 M7 21 H17"),
        ["servers"] = PathData.Parse("M3 3 H21 V10 H3 Z M3 14 H21 V21 H3 Z M7 6 V7 M7 17 V18 M11 6 H17 M11 17 H17"),
        ["account"] = PathData.Parse("M12 3 A4 4 0 1 0 12 11 A4 4 0 1 0 12 3 M4 22 V20 A8 8 0 0 1 20 20 V22"),
        ["more"] = PathData.Parse("M4 11 H6 V13 H4 Z M11 11 H13 V13 H11 Z M18 11 H20 V13 H18 Z"),
    };

    /// <summary>Creates a continuous outline icon for native menu and settings slots.</summary>
    public static IconElement Element(string name, float size = 20f, Brush? foreground = null) => new LauncherSettingsIcon(Paths[name], size)
    {
        Foreground = foreground
    };

    public static LauncherLineIcon Line(string name, float size = 22f, Brush? foreground = null) => new(Paths[name], size, name == "play", foreground);

    /// <summary>Returns shared geometry for primitive-only views that cannot mount an icon control.</summary>
    internal static PathData Geometry(string name) => Paths[name];
}

internal sealed class LauncherSettingsIcon(PathData data, float size) : IconElement
{
    protected override Widget BuildRenderCore(Brush? inheritedForeground)
    {
        var icon = new LineIconWidget(data, size, false, Foreground);
        icon.SetInheritedForeground(inheritedForeground);
        return icon;
    }
}