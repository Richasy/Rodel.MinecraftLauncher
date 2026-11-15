// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

// Only the packaged controller/window peers are replaced. Pointer routing, reactive selection,
// stroke drafts, RGBA and persistence use the production input owner and real Core session.
internal sealed class SkinEditorController(SkinEditingSession session, SkinEditorState state)
{
    internal SkinEditingSession? Session => session;
    internal SkinInputWindow Window { get; } = new();
    internal SkinInputLayerGate LayerActions { get; } = new();
    internal SkinInputFileGate Files { get; } = new();
    internal SkinInputTransfers Transfers { get; } = new();
    internal bool IsCommitting { get; set; }
    internal bool HasDeferredStroke { get; set; }
    internal Task Commit { get; private set; } = Task.CompletedTask;
    internal int FillRequests { get; private set; }
    internal event Action<bool>? CommitFinished;
    private Action? _draftDiscarded, _deferredRetryRequested;
    internal event Action? DraftDiscarded { add => _draftDiscarded += value; remove => _draftDiscarded -= value; }
    internal event Action? DeferredRetryRequested { add => _deferredRetryRequested += value; remove => _deferredRetryRequested -= value; }

    internal int Invalidations { get; private set; }
    internal void Invalidate() => Invalidations++;
    internal void Error(string message) => state.Status = message;

    internal Task CommitAsync() => Commit = SaveAsync();

    private async Task SaveAsync()
    {
        await session.CommitAsync();
        CommitFinished?.Invoke(true);
    }

    internal Task FillAsync(SkinHit _)
    {
        FillRequests++;
        return Task.CompletedTask;
    }

    internal SkinEdit FillEdit(SkinHit _) => throw new NotSupportedException("Fill preview is outside this input test boundary.");
}

internal sealed class SkinInputWindow
{
    private EventHandler? _deactivated;
    internal event EventHandler? Deactivated { add => _deactivated += value; remove => _deactivated -= value; }
}

internal sealed class SkinInputLayerGate
{
    internal bool CanPaint { get; set; } = true;
}

internal sealed class SkinInputFileGate
{
    internal bool Available { get; set; } = true;
}

internal sealed class SkinInputTransfers
{
    internal bool CanChange { get; set; }
    internal void BeginMove() => throw new NotSupportedException();
    internal void Place(int _, int __) => throw new NotSupportedException();
    internal void ConfirmPaste() => throw new NotSupportedException();
    internal void CancelPaste() => throw new NotSupportedException();
}

internal sealed class SkinPaletteController;
internal sealed class SkinEditorAiController;