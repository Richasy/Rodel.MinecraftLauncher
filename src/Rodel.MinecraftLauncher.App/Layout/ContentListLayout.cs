// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Layout;

namespace Rodel.MinecraftLauncher.App.Layout;

internal static class ContentListLayout
{
    internal const float ColumnSpacing = 12f;
    internal const float HorizontalPadding = 20f;

    internal static GridLength[] Columns(bool compact, float textScaleFactor) => compact
        ? [
            GridLength.Pixels(42f),
            GridLength.Star(),
            GridLength.Pixels(StatusWidth(textScaleFactor)),
            GridLength.Pixels(40f),
            GridLength.Pixels(32f),
        ]
        : [
            GridLength.Pixels(42f),
            GridLength.Star(),
            GridLength.Pixels(112f),
            GridLength.Pixels(StatusWidth(textScaleFactor)),
            GridLength.Pixels(40f),
            GridLength.Pixels(32f),
        ];

    private static float StatusWidth(float textScaleFactor) => 64f * TextScale.Resolve(11f, textScaleFactor) / 11f;
}