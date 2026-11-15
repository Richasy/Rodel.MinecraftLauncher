// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Semantics;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

/// <summary>Keeps the menu's remote avatar loading state inside the same fixed-size glyph slot.</summary>
internal sealed class AccountMenuAvatar : Panel
{
    private readonly ImageWidget _image;
    private readonly ShimmerWidget _shimmer;
    internal bool HasImage { get; private set; }

    internal AccountMenuAvatar(ImageSource fallback, bool loading)
    {
        _image = new(null, ImageDrawOptions.Default, fallback);
        _shimmer = new(
            Rect.FromXywh(0f, 0f, 28f, 28f),
            TimeSpan.FromSeconds(1.2),
            loading,
            CornerRadius.Zero,
            LauncherTheme.Muted.MultiplyAlpha(.14f),
            LauncherTheme.Ink.MultiplyAlpha(.18f));
        AttachChild(0, _shimmer);
        AttachChild(1, _image);
        _shimmer.SetVisibility(loading ? Visibility.Visible : Visibility.Collapsed);
        _image.SetVisibility(loading ? Visibility.Collapsed : Visibility.Visible);
    }

    internal void Complete(ImageSource? source)
    {
        if (source is not null)
        {
            _image.SetSource(source);
            HasImage = true;
        }

        _shimmer.SetIsActive(false);
        _shimmer.SetVisibility(Visibility.Collapsed);
        _image.SetVisibility(Visibility.Visible);
    }

    internal void ApplyAppearance(LauncherAppearance theme)
    {
        _shimmer.SetBaseColor(theme.Muted.MultiplyAlpha(.14f));
        _shimmer.SetHighlightColor(theme.Ink.MultiplyAlpha(.18f));
    }

    protected override Size MeasureOverride(BoxConstraints constraints, LayoutContext context)
    {
        _image.Measure(BoxConstraints.Tight(new(28f, 28f)), context);
        _shimmer.Measure(BoxConstraints.Tight(new(28f, 28f)), context);
        return new(28f, 28f);
    }

    protected override void ArrangeOverride(Rect bounds)
    {
        _image.Arrange(bounds);
        _shimmer.Arrange(bounds);
    }
}