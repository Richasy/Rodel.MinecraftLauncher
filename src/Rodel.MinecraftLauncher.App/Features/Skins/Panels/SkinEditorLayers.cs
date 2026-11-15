// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Commits layer commands through the same editor session and preserves a failed command for explicit recovery.</summary>
internal sealed class SkinEditorLayers(SkinEditorController controller, SkinEditorState state)
{
    private sealed record PendingChange(SkinEditingSession Session, SkinLayerChange Change);
    private PendingChange? _pending;
    private string _renameOriginal = string.Empty;
    internal event Action? CancelDrag;
    internal bool CanChange => !state.Ai.IsEditing && CanSubmitAi;
    internal bool CanSubmitAi => !state.Selecting && state.HasProject && !state.FileActive && !state.Busy && !state.LayerEditing && !state.LayerDragging && !state.LayerPending && !controller.HasDeferredStroke && (controller.Session?.HasDraft == false);
    internal bool CanPaint => !state.Ai.IsEditing && !state.FileActive && !state.LayerEditing && !state.LayerDragging && !state.LayerPending && (controller.Session?.CanPaint == true);

    internal void StopDrag() => CancelDrag?.Invoke();

    internal void Discard()
    {
        StopDrag();
        _pending = null;
        state.LayerPending = false;
        EndRename();
    }

    internal bool BeginRename(string id, string displayedName)
    {
        if (!CanChange)
            return false;
        controller.SelectLayer(id);
        state.LayerRenameId = id;
        state.LayerRenameName = displayedName;
        _renameOriginal = displayedName;
        state.LayerEditing = true;
        controller.Input.ClearHover();
        return true;
    }

    internal Task<bool> CommitRenameAsync()
    {
        if (!state.LayerEditing || state.Busy)
            return Task.FromResult(false);
        if (!state.LayerPending && (state.LayerRenameName == _renameOriginal))
        {
            EndRename();
            return Task.FromResult(true);
        }

        var name = state.LayerRenameName.Trim();
        return ApplyAsync(new()
        {
            Operation = SkinLayerOperation.Set,
            Id = state.LayerRenameId,
            Name = name
        }, rename: true);
    }

    private void EndRename()
    {
        state.LayerEditing = false;
        state.LayerRenameId = string.Empty;
    }

    internal Task<bool> AddAsync() => ApplyAsync(new()
    {
        Operation = SkinLayerOperation.Add,
        Id = Guid.NewGuid().ToString("N"),
        Name = string.Format(Resources.SkinLayerNewName, state.Layers.Length),
    });

    internal Task<bool> RetryAsync() => state.LayerEditing
        ? CommitRenameAsync()
        : _pending is { } pending ? SaveAsync(pending) : Task.FromResult(false);

    internal Task<bool> ApplyAsync(SkinLayerChange change, bool rename = false)
    {
        if (state.Selecting || state.Pasting || state.Busy || state.FileActive || state.LayerDragging || controller.HasDeferredStroke || controller.Session is not { HasDraft: false } session)
            return Task.FromResult(false);
        if ((state.LayerEditing || state.LayerPending) && !rename)
            return Task.FromResult(false);
        if (rename && string.IsNullOrWhiteSpace(change.Name))
        {
            state.Status = Resources.SkinLayerNameEmpty;
            return Task.FromResult(false);
        }

        if (_pending is { } pending && (!ReferenceEquals(pending.Session, session) || (pending.Change.Id != change.Id)))
            return Task.FromResult(false);
        return SaveAsync(new(session, change));
    }

    private async Task<bool> SaveAsync(PendingChange pending)
    {
        if (state.Busy || !ReferenceEquals(pending.Session, controller.Session))
            return false;
        state.Busy = true;
        _pending = pending;
        state.LayerPending = true;
        state.Status = Resources.SkinSaving;
        controller.Input.ClearHover();
        try
        {
            await Task.Run(() => pending.Session.ApplyAsync(new()
            {
                Tool = SkinTool.Layer,
                LayerChange = pending.Change
            }));
            _pending = null;
            state.LayerPending = false;
            if (pending.Change.Name is not null && (pending.Change.Id == state.LayerRenameId))
                EndRename();
            state.Status = Resources.SkinSaved;
            controller.Notify(refreshThumbnails: pending.Change.Operation is not (SkinLayerOperation.Set or SkinLayerOperation.Reorder));
            return true;
        }
        catch (Exception error)
        {
            state.Status = string.Format(Resources.SkinLayerSaveFailed, error.Message);
            return false;
        }
        finally
        {
            state.Busy = false;
        }
    }
}