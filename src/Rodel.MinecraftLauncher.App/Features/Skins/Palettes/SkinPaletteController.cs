// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Popups;
using Sprout.Reactive;
using Sprout.WindowsAppSDK;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Owns local palette preferences and their compact management popup, independently of skin transactions.</summary>
internal sealed class SkinPaletteController
{
    private readonly SkinEditorState _editor;
    private readonly AppSettings _settings;
    private readonly Signal<int> _version = new(0);
    private readonly Signal<string> _error = new(string.Empty);
    private SkinPaletteStore _store = null!;
    private bool _loadFailed;

    internal SkinPaletteController(SkinEditorState editor, AppSettings settings)
    {
        _editor = editor;
        _settings = settings;
        Reload();
    }

    internal SkinPalette[] Items
    {
        get
        {
            _ = _version.Value;
            return _store.Items;
        }
    }

    internal SkinPalette Current
    {
        get
        {
            _ = _version.Value;
            return _store.Current;
        }
    }

    internal bool IsBuiltIn
    {
        get
        {
            _ = _version.Value;
            return _store.IsBuiltIn;
        }
    }

    internal bool CanWrite
    {
        get
        {
            _ = _version.Value;
            return !_loadFailed;
        }
    }

    internal bool HasPalette => Current.Id.Length > 0;
    internal string Error => _error.Value;
    internal int SelectedIndex => Array.FindIndex(Items, palette => palette.Id == Current.Id);

    internal static string DisplayName(SkinPalette palette) => palette.Id switch
    {
        "" => Resources.SkinPaletteEmpty,
        "forest" => Resources.SkinPaletteForest,
        "sunset" => Resources.SkinPaletteSunset,
        "ocean" => Resources.SkinPaletteOcean,
        "pastel" => Resources.SkinPalettePastel,
        "neon" => Resources.SkinPaletteNeon,
        _ => palette.Name,
    };

    internal void Reload()
    {
        try
        {
            _store = new(_settings.SkinPalettes, value => _settings.SkinPalettes = value);
            _loadFailed = false;
            _error.Value = string.Empty;
        }
        catch (Exception error)
        {
            _store = new(string.Empty, value => _settings.SkinPalettes = value);
            _loadFailed = true;
            _error.Value = string.Format(Resources.SkinPaletteLoadFailed, error.Message);
        }

        _version.Value++;
    }

    internal bool Run(Action<SkinPaletteStore> change)
    {
        if (_loadFailed)
            return false;
        try
        {
            change(_store);
            _error.Value = string.Empty;
            _version.Value++;
            return true;
        }
        catch (Exception error)
        {
            _error.Value = string.Format(Resources.SkinPaletteSaveFailed, error.Message);
            return false;
        }
    }

    internal void Select(int index)
    {
        var items = Items;
        if ((uint)index < (uint)items.Length)
            Run(store => store.Select(items[index].Id));
    }

    internal void Pick(int index)
    {
        var colors = Current.Colors;
        if ((uint)index < (uint)colors.Length)
            _editor.Foreground = colors[index];
    }

    internal string NameError(string value, bool rename = false)
    {
        var name = value.Trim();
        if (name.Length == 0)
            return Resources.SkinPaletteNameEmpty;
        if (name.Length > 40)
            return Resources.SkinPaletteNameLong;
        if (Items.Any(palette => (!rename || palette.Id != Current.Id) && DisplayName(palette).Equals(name, StringComparison.OrdinalIgnoreCase)))
            return Resources.SkinPaletteNameExists;
        return string.Empty;
    }

    internal string SuggestName(bool copy, string? suggested = null)
    {
        var stem = copy ? string.Format(Resources.SkinPaletteCopyName, DisplayName(Current)) : suggested?.Trim();
        if (string.IsNullOrEmpty(stem))
            stem = Resources.SkinPaletteDefaultName;
        if (stem.Length > 40)
            stem = stem[..40];
        var name = stem;
        for (var number = 2; NameError(name).Length > 0; number++)
        {
            var suffix = string.Format(Resources.SkinPaletteNumberedName, string.Empty, number);
            name = string.Format(Resources.SkinPaletteNumberedName, stem[..Math.Min(stem.Length, 40 - suffix.Length)], number);
        }

        return name;
    }

    internal void Show(bool copyAndAdd = false) => Show(copyAndAdd ? SkinPaletteMode.CopyAndAdd : SkinPaletteMode.Manage);

    internal void Show(SkinPaletteMode mode, SkinPaletteReference? reference = null)
    {
        var window = _editor.Controller.Window;
        if (ModalPopup.IsOpen(window) || _editor.FileActive)
            return;
        var popup = new Flyout(close => new SkinPaletteDialog(_editor, close, mode, reference))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Padding(0f).MaxWidth(620f));
        ModalPopup.TryShow(window, popup);
    }

    internal async Task FromImageAsync()
    {
        var window = _editor.Controller.Window;
        if (!CanWrite || _editor.FileActive || ModalPopup.IsOpen(window))
            return;
        _editor.FileActive = true;
        var status = _editor.Status;
        SkinPaletteReference? reference = null;
        try
        {
            var picker = window.CreateFileOpenPicker();
            foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" })
                picker.FileTypeFilter.Add(extension);
            if (await picker.PickSingleFileAsync() is not { } file)
                return;
            _editor.Status = Resources.SkinPaletteExtracting;
            reference = await Task.Run(async () =>
            {
                var image = await SkinPaletteImage.ReadAsync(file.Path, CancellationToken.None).ConfigureAwait(false);
                return new SkinPaletteReference(Path.GetFileName(file.Path), image, image.CreateSource(), SkinPaletteQuantizer.Extract(image));
            });
            _error.Value = string.Empty;
        }
        catch (Exception error)
        {
            _error.Value = string.Format(Resources.SkinPaletteImageFailed, error.Message);
        }
        finally
        {
            _editor.FileActive = false;
            if (_editor.Status == Resources.SkinPaletteExtracting)
                _editor.Status = status;
        }

        if (reference is not null)
            Show(SkinPaletteMode.Reference, reference);
    }
}