// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Skins;
using Sprout;
using Sprout.Popups;
using Sprout.WindowsAppSDK;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Owns native file selection and one frozen import review; project mutations remain in Core.</summary>
internal sealed class SkinEditorFiles(SkinService service, SkinEditorController controller, SkinEditorState state)
{
    private SkinPngImport? _input;
    private bool _importCape;
    private SkinEditingSession? _importOwner;
    internal bool IsCompanionImport { get; private set; }
    private SkinEditingSession? _target, _result;
    private long _revision;
    private string _previousStatus = string.Empty;
    internal bool Available => !state.Ai.IsEditing && !state.Selecting && !state.Busy && !state.FileActive && !state.HasLayerDraft && !state.LayerDragging && !controller.HasDeferredStroke && (controller.Session?.HasDraft != true) && !ModalPopup.IsOpen(controller.Window);
    internal bool CanExport => Available && state.HasProject;
    internal string LastError { get; private set; } = string.Empty;

    internal void ShowImportLayer()
    {
        if (!CanExport || controller.Session is not { } session)
            return;
        state.FileActive = true;
        var revision = session.Revision;
        DispatcherQueue.GetForCurrentThread().TryEnqueue(() => _ = PickLayerAsync(session, revision));
    }

    private async Task PickLayerAsync(SkinEditingSession session, long revision)
    {
        state.Busy = true;
        try
        {
            var picker = controller.Window.CreateFileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;
            var input = await Task.Run(() => SkinService.PrepareImportAsync(file.Path, model: session.Render().Model));
            if (!ReferenceEquals(controller.Session, session) || session.Revision != revision)
                throw new InvalidOperationException(Resources.SkinPngProjectChanged);
            if (input.IsLegacy || input.Size != session.Render().Size || input.SourceHeight != session.Render().Height)
                throw new InvalidDataException(string.Format(Resources.SkinLayerPngSizeMismatch, session.Render().Size, session.Render().Height));
            await Task.Run(() => session.ImportLayerAsync(input, Path.GetFileNameWithoutExtension(file.Path)));
            state.Panel = 0;
            state.Status = Resources.SkinSaved;
            controller.Notify();
        }
        catch (Exception error)
        {
            state.Status = string.Format(Resources.SkinPngImportFailed, error.Message);
        }
        finally
        {
            state.Busy = false;
            state.FileActive = false;
        }
    }

    internal void ShowExportLayer(string layerId) => DispatcherQueue.GetForCurrentThread().TryEnqueue(() => _ = ExportAsync(layerId: layerId));

    internal void ShowOpenProject()
    {
        if (!Available)
            return;
        state.FileActive = true;
        DispatcherQueue.GetForCurrentThread().TryEnqueue(() => _ = PickProjectAsync());
    }

    private async Task PickProjectAsync()
    {
        state.Busy = true;
        try
        {
            var picker = controller.Window.CreateFileOpenPicker();
            picker.FileTypeFilter.Add(SkinService.ProjectFileExtension);
            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;
            var (session, name) = await Task.Run(() => service.OpenProjectFileAsync(file.Path));
            controller.Adopt(session, name);
            state.Status = Resources.SkinSaved;
        }
        catch (Exception error)
        {
            state.Status = string.Format(Resources.SkinProjectOpenFailed, error.Message);
        }
        finally
        {
            state.Busy = false;
            state.FileActive = false;
        }
    }

    internal void ShowImportCape() => BeginImport(true);

    internal void ShowImport() => BeginImport(state.IsCape);

    internal void ShowImportCompanion()
    {
        if (!state.IsCape || controller.Session is null)
            return;
        BeginImport(false, companion: true);
    }

    private void BeginImport(bool cape, bool companion = false)
    {
        if (!Available)
            return;
        state.FileActive = true;
        _previousStatus = state.Status;
        _importCape = cape;
        IsCompanionImport = companion;
        _importOwner = companion ? controller.Session : null;
        _target = !companion && controller.Session is { } current && current.Render().Model == SkinModel.Cape == cape ? current : null;
        _revision = (_importOwner ?? _target)?.Revision ?? 0;
        _result = null;
        // The application menu closes after invoking its callback; enter the native picker on the next turn.
        DispatcherQueue.GetForCurrentThread().TryEnqueue(() => _ = PickImportAsync());
    }

    private async Task PickImportAsync()
    {
        state.Busy = true;
        var reviewing = false;
        try
        {
            var picker = controller.Window.CreateFileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;
            _input = await Task.Run(() => SkinService.PrepareImportAsync(file.Path, model: _importCape ? Rodel.Minecraft.Skins.SkinModel.Cape : Rodel.Minecraft.Skins.SkinModel.Classic));
            var source = _input;
            var snapshots = await Task.Run(() => new[] { new SkinRenderSnapshot(source.Decode(_importCape ? SkinModel.Cape : SkinModel.Classic)), new SkinRenderSnapshot(source.Decode(_importCape
                                ? SkinModel.Cape
                                : source.IsLegacy ? SkinModel.Classic : SkinModel.Slim)), });
            var previews = await Task.Run(() => snapshots.Select(snapshot => SkinLayerThumbnail.Create(snapshot.Pixels.ToArray(), snapshot.Size, snapshot.Size)).ToArray());
            var popup = new Flyout(close => new SkinPngImportDialog(
                    this,
                    state,
                    source,
                    previews,
                    snapshots,
                    file.Path,
                    close))
            {
                Placement = PopupPlacement.Full,
                IsModal = true,
                IsLightDismissEnabled = false
            }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Padding(24f).MaxWidth(820f));
            popup.Closing += args => args.Cancel = state.Busy;
            popup.Closed += () => FinishImport();
            state.Busy = false;
            reviewing = ModalPopup.TryShow(controller.Window, popup);
        }
        catch (Exception error)
        {
            state.Status = string.Format(Resources.SkinPngImportFailed, error.Message);
        }
        finally
        {
            state.Busy = false;
            if (!reviewing)
            {
                state.FileActive = false;
                _input = null;
                _target = _result = _importOwner = null;
            }
        }
    }

    internal async Task<bool> ConfirmImportAsync(SkinModel model, string name)
    {
        if (state.Busy || !state.FileActive || _input is null)
            return false;
        state.Busy = true;
        LastError = string.Empty;
        try
        {
            if (_result is null)
            {
                var observed = _importOwner ?? _target;
                if (observed is not null && (!ReferenceEquals(controller.Session, observed) || observed.Revision != _revision))
                {
                    throw new InvalidOperationException(Resources.SkinPngProjectChanged);
                }

                if (_target is null)
                {
                    _result = IsCompanionImport
                        ? await Task.Run(() => service.CreateCapePreviewSkinAsync(_input, model, name))
                        : await Task.Run(() => service.CreateImportedAsync(_input, model, name));
                }
                else
                {
                    await Task.Run(() => _target.ImportAsync(_input, model));
                    _result = _target;
                }
            }

            var result = _result ?? throw new InvalidOperationException(Resources.SkinPngProjectChanged);
            if (IsCompanionImport)
            {
                await controller.Outfit.UseImportedAsync(result);
            }
            else
            {
                controller.Adopt(result, _target is null ? name.Trim() : state.ProjectName);
            }

            state.Status = Resources.SkinSaved;
            return true;
        }
        catch (Exception error)
        {
            LastError = string.Format(Resources.SkinPngImportFailed, error.Message);
            state.Status = LastError;
            return false;
        }
        finally
        {
            state.Busy = false;
        }
    }

    private void FinishImport()
    {
        if (_result is null)
            state.Status = _previousStatus;
        state.FileActive = false;
        _input = null;
        _target = _result = _importOwner = null;
    }

    internal void ShowSaveAs()
    {
        if (!CanExport)
            return;
        state.FileActive = true;
        DispatcherQueue.GetForCurrentThread().TryEnqueue(() => _ = PickSaveAsAsync());
    }

    private async Task PickSaveAsAsync()
    {
        state.Busy = true;
        var previous = state.Status;
        try
        {
            var session = controller.Session ?? throw new InvalidOperationException(Resources.SkinNoLayers);
            var picker = controller.Window.CreateFileSavePicker();
            picker.SuggestedFileName = SuggestedFileName(state.ProjectName);
            string[] extensions = [SkinService.ProjectFileExtension];
            picker.FileTypeChoices.Add(Resources.SkinProjectFileType, extensions);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                state.Status = previous;
                return;
            }

            await Task.Run(() => session.SaveProjectAsAsync(file.Path, overwrite: true));
            state.Status = string.Format(Resources.SkinProjectSavedAs, file.Path);
        }
        catch (Exception error)
        {
            state.Status = string.Format(Resources.SkinProjectSaveAsFailed, error.Message);
        }
        finally
        {
            state.Busy = false;
            state.FileActive = false;
        }
    }

    internal async Task ExportAsync(int? size = null, string? layerId = null)
    {
        if (!CanExport || controller.Session is not { } session)
            return;
        state.FileActive = true;
        state.Busy = true;
        var previous = state.Status;
        try
        {
            var picker = controller.Window.CreateFileSavePicker();
            picker.SuggestedFileName = SuggestedFileName(layerId is null ? state.ProjectName : session.Layers.First(layer => layer.Id == layerId).Name) + (size == 64 && state.TextureSize == 128 ? (state.IsCape ? "-64x32" : "-64x64") : string.Empty);
            string[] extensions = [".png"];
            picker.FileTypeChoices.Add(Resources.SkinPngFileType, extensions);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                state.Status = previous;
                return;
            }
            // The native save picker owns overwrite confirmation; Core atomically replaces that explicit destination.
            await Task.Run(() => layerId is null ? session.ExportAsync(file.Path, size ?? state.TextureSize, overwrite: true) : session.ExportLayerAsync(layerId, file.Path, overwrite: true));
            state.Status = string.Format(Resources.SkinPngExported, file.Path);
        }
        catch (Exception error)
        {
            state.Status = string.Format(Resources.SkinPngExportFailed, error.Message);
        }
        finally
        {
            state.Busy = false;
            state.FileActive = false;
        }
    }

    private static string SuggestedFileName(string name)
    {
        var fileName = string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).TrimEnd(' ', '.');
        return string.IsNullOrWhiteSpace(fileName) ? "skin" : fileName;
    }
}