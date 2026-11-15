// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Instances;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

[View]
internal readonly partial record struct ContentIconView(
    byte[]? Bytes,
    string? Warning,
    string FailedText,
    Color Ink,
    Color Edge)
{
    internal ImageSource Source => Bytes is null ? InstanceIconView.Transparent : ImageSource.FromBytes(Bytes, 128, 128);

    public Ui Body => When(Bytes is not null, new ContentLoadedIcon(Source, FailedText, Ink, Edge), new ContentFallbackIcon(Warning, Ink, Edge));
}

[View]
internal readonly partial record struct ContentLoadedIcon(
    ImageSource Source,
    string FailedText,
    Color Ink,
    Color Edge)
{
    public Ui Body => AsyncImage(Source)
        .Stretch(ImageStretch.Uniform)
        .Placeholder(new ContentIconLoading())
        .Failed(new ContentFallbackIcon(FailedText, Ink, Edge))
        .Width(42f).Height(42f);
}

[View]
internal readonly partial record struct ContentRemoteIconView(
    string? Url,
    string FailedText,
    Color Ink,
    Color Edge)
{
    internal bool HasSource => Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    internal ImageSource Source => HasSource ? ImageSource.FromUri(new Uri(Url!, UriKind.Absolute), decodePixelWidth: 128, decodePixelHeight: 128, cacheBucket: "content-icons") : InstanceIconView.Transparent;

    public Ui Body => When(HasSource, new ContentLoadedIcon(Source, FailedText, Ink, Edge), new ContentFallbackIcon(string.IsNullOrWhiteSpace(Url) ? null : FailedText, Ink, Edge));
}

[View]
internal readonly partial record struct ContentIconLoading
{
    public Ui Body => Shimmer(new Size(42f, 42f))
        .CornerRadius(0f)
        .Automation(new()
        {
            Name = Resources.ContentIconLoading,
            AutomationId = "content.icon.loading"
        });
}

[View]
internal readonly partial record struct ContentFallbackIcon(string? Warning, Color Ink, Color Edge)
{
    public Ui Body => Overlay([
        Border(Viewbox(Sprout.Markup.Path(LauncherIcons.Geometry("instances"))
                    .Stroke(Ink, 1.7f, StrokeStyle.Solid with
                    {
                        StartCap = PenLineCap.Round,
                        EndCap = PenLineCap.Round,
                        LineJoin = PenLineJoin.Round,
                    })
                    .Width(24f).Height(24f))
                .Width(28f).Height(28f)
                .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
            .BorderBrush(Edge)
            .BorderThickness(1f)
            .ToolTip(Warning),
    ])
        .Width(42f).Height(42f);
}