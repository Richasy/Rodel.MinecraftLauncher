// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Skins;
using Sprout;
using Sprout.Controls;
using Sprout.Popups;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Coordinates project boundaries and durable brush transactions; GPU and input remain viewport-owned.</summary>
internal sealed class SkinEditorController(SkinService service, Window window, SkinEditorState state, AppSettings settings)
{
    internal Window Window => window;
    internal float DialogHeight => state.Height;
    internal static string[] Templates => SkinService.Templates;
    internal SkinEditingSession? Session { get; private set; }
    private SkinEditorInput? _input;
    internal SkinEditorTransfers Transfers { get => field ??= new(this, state); } = null!;
    private SkinEditingSession? _selectionSession;
    private SkinModel _selectionModel;
    private int _selectionSize;
    internal SkinEditorOutfit Outfit { get => field ??= new(service, this, state, settings); } = null!;
    internal SkinEditorFiles Files { get => field ??= new(service, this, state); } = null!;
    internal SkinEditorLayers LayerActions { get => field ??= new(this, state); } = null!;
    internal SkinEditorInput Input => _input ??= new(this, state);
    internal bool IsCommitting { get; private set; }
    internal bool HasDeferredStroke { get; set; }
    internal event Action<bool>? CommitFinished;
    internal event Action? DraftDiscarded;
    internal event Action? DeferredRetryRequested;
    internal event Action? Changed;

    internal void Invalidate() => Changed?.Invoke();

    internal void Notify(bool refreshThumbnails = true)
    {
        _input?.ClearHover();
        var layers = Session?.Layers ?? [];
        if (Session is { } current)
        {
            var render = current.Render();
            if (!ReferenceEquals(current, _selectionSession) || (render.Model != _selectionModel) || (render.Size != _selectionSize))
                state.Selection = null;
            _selectionSession = current;
            _selectionModel = render.Model;
            _selectionSize = render.Size;
            state.DocumentModel = render.Model;
            state.TextureSize = render.Size;
        }

        state.CanUndo = Session?.CanUndo == true;
        state.CanRedo = Session?.CanRedo == true;
        state.ActiveLayerId = Session?.ActiveLayerId ?? SkinDocument.BackgroundLayerId;
        var thumbnails = !refreshThumbnails
            ? state.LayerThumbnails
            : Session is { } session ? layers.ToDictionary(layer => layer.Id, layer => SkinLayerThumbnail.Create(session.LayerPixels(layer.Id), session.Render().Size)) : [];
        state.SetLayers(layers, thumbnails);
        state.History = Session?.History() ?? (0, []);
        Changed?.Invoke();
    }

    internal void SelectLayer(string id)
    {
        if (state.Selecting || state.Busy || HasDeferredStroke || Session is null || Session.HasDraft || state.HasLayerDraft || state.FileActive || state.LayerDragging)
            return;
        try
        {
            Session.SelectLayer(id);
            state.ActiveLayerId = id;
            Input.ClearHover();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
    }

    internal SkinEdit FillEdit(SkinHit hit) => new()
    {
        Tool = SkinTool.Fill,
        Surface = hit.Surface,
        X = hit.X,
        Y = hit.Y,
        Color = state.Foreground,
        PaintLayerId = Session!.ActiveLayerId,
        PixelMask = state.Selection?.CopyMask(),
        Mirror = state.MirrorMode,
        MirrorParts = state.MirrorParts,
    };

    internal bool CanCopyToOpposite => state.ShowCopyToOpposite && LayerActions.CanChange && LayerActions.CanPaint;

    internal async Task CopyToOppositeAsync()
    {
        if (!CanCopyToOpposite || state.IsolatedLimb is not { } part || Session is not { } session)
            return;
        var includeOuter = state.OuterVisible;
        state.Busy = true;
        Input.ClearHover();
        try
        {
            var skipWarning = settings.SkipSkinCopyOppositeWarning;
            if (!skipWarning)
            {
                var confirmed = false;
                ContentDialog? dialog = null;
                var content = new SkinPartCopyDialog(value => { confirmed = value; dialog?.Hide(); });
                dialog = new ContentDialog
                {
                    ContentComponent = () => content,
                    InitialFocus = () => content.CancelFocus,
                }.Style(LauncherMenus.Confirmation.ContentPadding(0f).MinWidth(320f))
                    .Automation(new() { Name = Resources.SkinCopyOpposite, AutomationId = "skin.copy-opposite.confirmation", IsDialog = true });
                await dialog.ShowAsync(window);
                if (!confirmed)
                    return;
                skipWarning = content.SkipWarning;
            }

            await Task.Run(() => session.CopyPartToOppositeAsync(part, includeOuter));
            state.Status = Resources.SkinSaved;
            Notify();
            if (skipWarning)
                settings.SkipSkinCopyOppositeWarning = true;
        }
        catch (Exception error)
        {
            Error(error.Message);
        }
        finally
        {
            state.Busy = false;
        }
    }

    internal async Task FillAsync(SkinHit hit)
    {
        if (!LayerActions.CanPaint)
        {
            state.Status = Resources.SkinLayerUnavailable;
            return;
        }

        if (state.Busy || HasDeferredStroke || Session is null || Session.HasDraft)
            return;
        state.Busy = true;
        try
        {
            var session = Session;
            var edit = FillEdit(hit);
            await Task.Run(() => session.ApplyAsync(edit));
            state.Status = Resources.SkinSaved;
            Notify();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        finally
        {
            state.Busy = false;
        }
    }

    internal void Error(string message) => state.Status = message;

    internal void Adopt(SkinEditingSession session, string name)
    {
        Session = session;
        state.Selection = null;
        _input?.ClearHover();
        state.ProjectName = name;
        state.HasProject = true;
        state.Parts = session.Render().Model == SkinModel.Cape ? 64 : 63;
        state.OuterSurface = false;
        _ = Outfit.LoadAsync(session);
        Notify();
    }

    internal async Task CreateAsync(
        string name,
        string? template,
        SkinModel model,
        int size)
    {
        if (state.Ai.IsEditing)
            return;
        if (state.Busy || HasDeferredStroke || (Session?.HasDraft == true) || state.HasLayerDraft || state.FileActive || state.LayerDragging)
            return;
        state.Busy = true;
        try
        {
            var layerName = Resources.SkinDrawingLayer;
            var id = await Task.Run(() => service.CreateAsync(model, template: template, name: name, size: size));
            await Task.Run(() => service.EditAsync(id, 0, [new()
                        {
                            Tool = SkinTool.Layer,
                            LayerChange = new()
                            {
                                Operation = SkinLayerOperation.Add,
                                Id = "drawing",
                                Name = layerName
                            }
                        }]));
            Adopt(await Task.Run(() => service.OpenEditor(id)), name);
            state.Status = Resources.SkinSaved;
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        finally
        {
            state.Busy = false;
        }
    }

    internal async Task OpenAsync(string id, string name)
    {
        if (state.Ai.IsEditing)
            return;
        if (state.Busy || HasDeferredStroke || (Session?.HasDraft == true) || state.HasLayerDraft || state.FileActive || state.LayerDragging)
            return;
        state.Busy = true;
        try
        {
            Adopt(await Task.Run(() => service.OpenEditor(id)), name);
            state.Status = Resources.SkinSaved;
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        finally
        {
            state.Busy = false;
        }
    }

    internal async Task CommitAsync()
    {
        if (state.Selecting || state.Pasting || state.Busy || (Session?.HasDraft != true))
            return;
        state.Busy = true;
        IsCommitting = Session.HasStroke;
        state.Status = Resources.SkinSaving;
        var succeeded = false;
        try
        {
            var session = Session;
            await Task.Run(() => session.CommitAsync());
            succeeded = true;
            state.Status = Resources.SkinSaved;
            Notify();
        }
        catch (Exception ex)
        {
            Error(string.Format(Resources.SkinDraftSaveFailed, ex.Message));
        }
        finally
        {
            state.Busy = false;
            IsCommitting = false;
            CommitFinished?.Invoke(succeeded);
        }
    }

    internal Task RetryAsync()
    {
        if (state.Selecting || state.Pasting || state.Busy || state.FileActive)
            return Task.CompletedTask;
        if (state.LayerPending)
            return LayerActions.RetryAsync();
        if (Session?.HasDraft == true)
            return CommitAsync();
        DeferredRetryRequested?.Invoke();
        return Task.CompletedTask;
    }

    internal void Cancel()
    {
        if (state.Busy || state.FileActive)
            return;
        if (Session is not { } session)
            return;
        if (state.Pasting)
        {
            Input.Escape();
            return;
        }

        if (Input.HasGesture)
            Input.Escape();
        LayerActions.Discard();
        DraftDiscarded?.Invoke();
        HasDeferredStroke = false;
        session.Cancel();
        _ = OpenAsync(session.Id, state.ProjectName);
    }

    internal async Task RestoreAsync(bool redo)
    {
        if (state.Ai.IsEditing)
            return;
        if (state.Selecting || state.HasLayerDraft || state.FileActive || state.LayerDragging)
            return;
        if (state.Busy || HasDeferredStroke || Session is null || Session.HasDraft)
            return;
        state.Busy = true;
        try
        {
            var session = Session;
            await Task.Run(() => session.RestoreAsync(redo));
            state.Status = Resources.SkinSaved;
            Notify();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        finally
        {
            state.Busy = false;
        }
    }

    internal async Task RestoreToAsync(int index, long revision)
    {
        if (!LayerActions.CanChange || Session is not { } session)
            return;
        state.Busy = true;
        Input.ClearHover();
        try
        {
            await Task.Run(() => session.RestoreToAsync(index, revision));
            state.Status = Resources.SkinSaved;
            Notify();
        }
        catch (Exception error)
        {
            Error(error.Message);
        }
        finally
        {
            state.Busy = false;
        }
    }

    internal void ShowNew(int template = 0)
    {
        if (state.Ai.IsEditing)
            return;
        if (state.Selecting || state.HasLayerDraft || state.FileActive || state.LayerDragging)
            return;
        if ((Session?.HasDraft == true) || HasDeferredStroke || state.Busy)
            return;
        var popup = new Flyout(close => new SkinNewProject(this, close, template))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Padding(24f).MaxWidth(820f));
        ModalPopup.TryShow(window, popup);
    }

    internal Task<SkinProjectInfo[]> RecentProjectsAsync() => Task.Run(service.List);

    internal void ShowPreview()
    {
        if (Session is null || state.Selecting || state.Busy || state.FileActive || state.LayerDragging || ModalPopup.IsOpen(window))
            return;
        var snapshot = Session.Render();
        var popup = new Flyout(close => new SkinAnimationDialog(state, snapshot, close))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Background(popupAppearance.Background).Padding(0f).MaxWidth(840f));
        ModalPopup.TryShow(window, popup);
    }

    internal void ShowProjects() => Files.ShowOpenProject();
}