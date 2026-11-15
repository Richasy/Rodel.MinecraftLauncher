// Copyright (c) Richasy. All rights reserved.

using Sprout.Input;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed class ContentDropTarget(ContentPage page) : IDropHandler
{
    private bool Accept(DragDropEventArgs e) => page.CanDrop && e.Data.HasFiles && (e.AllowedEffects & DragEffect.Copy) != 0;

    public void OnDragEnter(DragDropEventArgs e) => OnDragOver(e);

    public void OnDragOver(DragDropEventArgs e)
    {
        var accepted = Accept(e);
        e.AcceptedEffect = accepted ? DragEffect.Copy : DragEffect.None;
        page.SetDropping(accepted);
    }

    public void OnDragLeave() => page.SetDropping(false);

    public void OnDrop(DragDropEventArgs e)
    {
        var accepted = Accept(e);
        e.AcceptedEffect = accepted ? DragEffect.Copy : DragEffect.None;
        page.SetDropping(false);
        if (accepted)
            page.DropFiles([.. e.Data.Files.Distinct(StringComparer.OrdinalIgnoreCase)]);
    }
}