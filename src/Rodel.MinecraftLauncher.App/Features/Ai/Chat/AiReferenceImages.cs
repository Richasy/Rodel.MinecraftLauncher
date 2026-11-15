// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Shows the immutable reference thumbnails used for a draft or an earlier message.</summary>
internal sealed partial class AiReferenceImages(
    AiChatImage[] images,
    Func<string, byte[]> read,
    Action<string>? remove = null,
    bool enabled = true) : Control
{
    private partial AiChatImage[] Images { get; set; }
    private partial Action<string>? Remove { get; set; }
    private partial bool Enabled { get; set; }
    private AiChatImage[] CurrentImages => Images ?? images;
    private Action<string>? RemoveImage => Remove ?? remove;
    private bool IsEnabled => Images is null ? enabled : Enabled;

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is not AiReferenceImages other)
            return;
        Images = other.CurrentImages;
        Remove = other.RemoveImage;
        Enabled = other.IsEnabled;
    }

    private Ui Body => ItemsRepeater(
        CurrentImages,
        static image => image.Id,
        image => new AiReferenceRow(image, read, RemoveImage, IsEnabled),
        (view, image) => view.Update(image, RemoveImage, IsEnabled),
        new Sprout.Widgets.StackLayout
        {
            EstimatedItemExtent = 56f,
            Spacing = 6f
        }).KeyboardNavigation();
}