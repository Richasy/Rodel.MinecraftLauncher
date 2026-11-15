// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.Core.Skins;
using Sprout.Reactive;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Applies the shared character choice to cape previews while keeping solo viewing local to the current editor.</summary>
internal sealed class SkinEditorOutfit(SkinService service, SkinEditorController controller, SkinEditorState state, AppSettings settings)
{
    private readonly Signal<SkinRenderSnapshot?> _reference = new(null);
    private readonly Signal<SkinProjectInfo?> _work = new(null);
    private readonly Signal<SkinProjectInfo[]> _choices = new([]);
    private readonly Signal<bool> _solo = new(false);
    private int _loadVersion;
    internal SkinProjectInfo[] Choices => _choices.Value;
    internal SkinRenderSnapshot? Reference => state.IsCape && !_solo.Value ? _reference.Value : null;
    internal SkinProjectInfo? Work => _work.Value;
    internal bool Solo
    {
        get => _solo.Value;
        set
        {
            _solo.Value = value;
            controller.Invalidate();
        }
    }

    internal async Task LoadAsync(SkinEditingSession session)
    {
        var version = ++_loadVersion;
        SetWork(null, null);
        _choices.Value = [];
        if (session.Render().Model != SkinModel.Cape)
            return;

        try
        {
            var preferred = settings.CapePreviewSkin;
            var (choices, work) = await Task.Run(() =>
            {
                var choices = service.ListCapePreviewSkins();
                var firstUse = preferred == AppSettings.UninitializedCapePreviewSkin;
                var id = firstUse && choices.Length > 0 ? service.Describe(session.Id).CompanionId : preferred;
                var work = choices.FirstOrDefault(choice => choice.DocumentId == id);
                return (choices, firstUse ? work ?? choices.FirstOrDefault() : work);
            });
            if (!IsCurrent(version, session))
                return;
            _choices.Value = choices;
            if (work is null)
                return;

            var reference = await Task.Run(() => ReadReference(work));
            if (IsCurrent(version, session))
                Select(work, reference);
        }
        catch (Exception error)
        {
            if (IsCurrent(version, session))
                controller.Error(string.Format(Resources.SkinOutfitFailed, error.Message));
        }
    }

    internal async Task SelectAsync(SkinProjectInfo? work)
    {
        if (!state.IsCape || !controller.Files.Available)
            return;
        state.Busy = true;
        try
        {
            var reference = work is null ? null : await Task.Run(() => ReadReference(work));
            Select(work, reference);
        }
        catch (Exception error)
        {
            controller.Error(string.Format(Resources.SkinOutfitFailed, error.Message));
        }
        finally
        {
            state.Busy = false;
        }
    }

    internal async Task UseImportedAsync(SkinEditingSession imported)
    {
        var choices = await Task.Run(service.ListCapePreviewSkins);
        var work = choices.First(choice => choice.DocumentId == imported.Id);
        Select(work, imported.Render());
        _choices.Value = choices;
    }

    internal void OpenWork()
    {
        if (state.IsCape && controller.Files.Available && Work is { } work)
            _ = controller.OpenAsync(work.DocumentId, work.Name);
    }

    private bool IsCurrent(int version, SkinEditingSession session) =>
        version == _loadVersion && ReferenceEquals(session, controller.Session);

    private SkinRenderSnapshot ReadReference(SkinProjectInfo work)
    {
        var document = service.Read(work.DocumentId);
        if (document.Model == SkinModel.Cape)
            throw new InvalidDataException("A cape preview uses a character skin.");
        return new(document);
    }

    private void Select(SkinProjectInfo? work, SkinRenderSnapshot? reference)
    {
        settings.CapePreviewSkin = work?.DocumentId ?? string.Empty;
        ++_loadVersion;
        SetWork(work, reference);
        controller.Invalidate();
    }

    private void SetWork(SkinProjectInfo? work, SkinRenderSnapshot? reference)
    {
        _reference.Value = reference;
        _work.Value = work;
        _solo.Value = false;
    }
}