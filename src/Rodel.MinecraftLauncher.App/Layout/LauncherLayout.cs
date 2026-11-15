// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Layout;

internal readonly record struct LauncherLayout(float Width, float Height)
{
    internal const float OuterInset = 36f;
    internal const float NavigationWidth = 72f;
    internal const float NavigationItemWidth = 58f;
    internal const float NavigationLabelWidth = 46f;
    internal const float AvatarGraphicSize = 40f * NavigationWidth / 64f;
    internal const float NavigationTop = OuterInset + NavigationWidth + 16f;
    internal bool Compact => Width <= 1100f;
    internal float HomeLeft => Compact ? 122f : 150f;
    internal float InstancePanelWidth => Compact ? 258f : 292f;
    internal const float InstancePanelInset = 14f;
    internal float InstancePanelContentWidth => InstancePanelWidth - (2f * InstancePanelInset) - 2f;
    internal float HomeBottom => Height - Math.Max(OuterInset + 24f, Height * .10f);
    internal const float PageHeaderHeight = NavigationWidth;
    internal const float PageHeaderGap = 16f;
    internal const float AiConversationMaxWidth = 820f;
    internal float PageHeight => Height - (2f * OuterInset);
    internal static Thickness PageMargin => new(148f, OuterInset, 40f, OuterInset);
    internal float HeroWidth => Math.Min(
        Math.Min(570f, Width * .47f),
        Width - HomeLeft - OuterInset - InstancePanelWidth - 28f);
    internal float HeadingSize => Math.Clamp(Width * .0425f, 42f, 68f);
    internal Thickness HomeMargin => new(HomeLeft, NavigationTop, OuterInset, Height - HomeBottom);
    internal Thickness EditorMargin => new(HomeLeft, NavigationTop, 0f, Height - HomeBottom);
    internal static Thickness NavigationMargin => new(OuterInset, OuterInset, 0f, 0f);
}