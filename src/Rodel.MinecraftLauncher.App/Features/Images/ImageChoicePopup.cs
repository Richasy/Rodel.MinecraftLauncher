// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Images;

/// <summary>One frozen choice, committed by its owning account or instance feature.</summary>
internal sealed record ImageChoice(string? Preset, byte[]? Png);

/// <summary>Stages preset or decoded/cropped images; only Save invokes the feature's persistence callback.</summary>
internal sealed partial class ImageChoicePopup : Control, IDropHandler
{
    internal readonly Window _window;
    internal readonly string _name;
    internal readonly bool _avatar;
    internal readonly ImagePreset[] _presets;
    internal readonly string? _original;
    internal readonly ImageSource _initial;
    private readonly Func<ImageChoice, CancellationToken, Task> _save;
    private readonly Action _close;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PopupFocusRecovery _focus;
    private readonly ImageChoiceBody _body;
    private ImageLoadServices? _images;
    internal ImageCrop? _crop;
    private int _sourceSide;
    private bool _disposed;
    private long _read;
    internal partial bool Custom { get; set; }
    internal partial bool Loading { get; set; }
    internal partial bool Saving { get; set; }
    internal partial bool Dropping { get; set; }
    internal partial string? Selected { get; set; }
    internal partial string Error { get; set; }
    internal partial string Label { get; set; }
    internal partial ImageSource Preview { get; set; }
    internal partial double Zoom { get; set; }
    internal partial Size Viewport { get; set; }
    internal bool CanClose => !Saving;
    internal IDropHandler DropHandler => this;

    internal ImageChoicePopup(
        Window window,
        string name,
        bool avatar,
        string? selected,
        ImageSource initial,
        Func<ImageChoice, CancellationToken, Task> save,
        Action close,
        string? error = null)
    {
        _window = window;
        _name = name;
        _avatar = avatar;
        _presets = avatar ? ImagePresetCatalog.Avatars : ImagePresetCatalog.Instances;
        _original = selected;
        _initial = initial;
        _save = save;
        _close = close;
        Selected = selected;
        Preview = initial;
        Label = selected is null ? Resources.ImageCustomLabel : _presets.First(p => p.Id == selected).Name;
        Custom = selected is null;
        Error = error ?? string.Empty;
        Zoom = 1;
        Viewport = window.ClientSize;
        _focus = new(window);
        _body = new(this);
    }

    internal float Width => Math.Min(440f, Viewport.Width - 48f);
    private bool Changed => Selected != _original || (Selected is null && _crop is not null);

    private Ui Body => Column([
        Border(Text(_avatar ? Resources.ImageAvatarTitle : Resources.InstanceIcon)
                .FontSize(16f).FontWeight(600).Foreground(LauncherTheme.Ink))
            .Padding(new Thickness(16f, 12f, 16f, 12f))
            .BorderBrush(LauncherTheme.Edge)
            .BorderThickness(0f, 0f, 0f, 1f),
        ScrollView(_body)
            .MaxHeight(Math.Max(100f, Viewport.Height - 200f))
            .Automation(new()
            {
                Name = _avatar ? Resources.ImageAvatarTitle : Resources.InstanceIcon,
                AutomationId = "image.scroll"
            }),
        Border(Row([
                LauncherControls.Command(Resources.ImageSave, () => _ = SaveAsync())
                    .Style(LauncherButtons.DialogPrimary)
                    .Weight(1f)
                    .Enabled(Changed && !Loading && !Saving)
                    .Automation(new()
                    {
                        Name = Resources.ImageSave,
                        AutomationId = "image.save"
                    }),
                LauncherControls.OutlineCommand(Resources.DialogCancel, _close)
                    .Weight(1f)
                    .Enabled(!Saving)
                    .Automation(new()
                    {
                        Name = Resources.DialogCancel,
                        AutomationId = "image.cancel"
                    }),
            ])
                .Spacing(12f))
            .Padding(new Thickness(16f, 12f, 16f, 12f))
            .BorderBrush(LauncherTheme.Edge)
            .BorderThickness(0f, 1f, 0f, 0f),
    ])
        .Width(Width);

    internal void Select(ImagePreset preset)
    {
        if (Saving)
            return;
        Selected = preset.Id;
        Preview = preset.Source;
        Label = preset.Name;
        Error = string.Empty;
        Custom = false;
    }

    internal void ChooseTab(bool custom)
    {
        Custom = custom;
        if (custom && _crop is not null)
        {
            RefreshCrop();
        }
        else if (custom && _original is null)
        {
            Selected = null;
            Preview = _initial;
            Label = Resources.ImageCustomLabel;
        }
    }

    internal void ChooseSource(int index) => ChooseTab(index == 1);

    internal void SetZoom(double value)
    {
        if (_crop is null || Saving)
            return;
        _crop.SetZoom(value);
        Zoom = _crop.Zoom;
        RefreshCrop();
    }

    internal void Move(double x, double y)
    {
        if (_crop is null || Saving)
            return;
        _crop.Move(x * _sourceSide, y * _sourceSide);
        RefreshCrop();
    }

    private void RefreshCrop()
    {
        if (_crop is null)
            return;
        Selected = null;
        Preview = ImageSource.FromPixels(new(ImageCrop.OutputSize, ImageCrop.OutputSize, _crop.Render()));
        Label = Resources.ImageCustomLabel;
    }

    internal async Task BrowseAsync()
    {
        if (Loading || Saving)
            return;
        var before = _window.FocusedElement;
        Loading = true;
        try
        {
            var picker = _window.CreateFileOpenPicker();
            foreach (var type in new[]
            {
                ".png",
                ".jpg",
                ".jpeg",
                ".webp"
            }
            )
            {
                picker.FileTypeFilter.Add(type);
            }

            if (await picker.PickSingleFileAsync() is { } file && !_disposed)
                await ReadAsync(file.Path);
        }
        catch (Exception)
        {
            if (!_disposed)
                Error = Resources.ImageReadError;
        }
        finally
        {
            if (!_disposed)
            {
                Loading = false;
                _focus.Request(this, before);
            }
        }
    }

    private async Task ReadAsync(string path)
    {
        if (_images is null)
            return;
        var read = ++_read;
        Loading = true;
        Error = string.Empty;
        try
        {
            if (!new[]
            {
                ".png",
                ".jpg",
                ".jpeg",
                ".webp"
            }.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException();
            }

            var image = await Task.Run(() => _images.Decoder.Decode(ImageSource.FromFile(path), new(1024, 1024)), _lifetime.Token);
            if (_disposed || read != _read)
                return;
            _crop = new(image.Width, image.Height, [.. image.Pixels]);
            _sourceSide = Math.Min(image.Width, image.Height);
            Zoom = 1;
            RefreshCrop();
        }
        catch (Exception)
        {
            if (!_disposed && read == _read)
                Error = Resources.ImageReadError;
        }
        finally
        {
            if (!_disposed && read == _read)
                Loading = false;
        }
    }

    private async Task SaveAsync()
    {
        if (Saving || Loading || !Changed)
            return;
        Saving = true;
        Error = string.Empty;
        try
        {
            var png = Selected is null && _crop is not null ? await ImagePng.EncodeAsync(_crop.Render()) : null;
            _lifetime.Token.ThrowIfCancellationRequested();
            await _save(new(Selected, png), _lifetime.Token);
            Saving = false;
            _close();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (!_disposed)
                Error = Resources.ImageSaveError;
        }
        finally
        {
            if (!_disposed)
                Saving = false;
        }
    }

    private void SizeChanged(object? sender, WindowSizeChangedEventArgs args) => Viewport = args.Size;

    protected override IDisposable? OnMounted()
    {
        _images = Sprout.Theming.Environment.Resolve(ImageLoadServices.Key);
        _window.SizeChanged += SizeChanged;
        return PopupKeyboardClose.Bind(_window, _close, () => CanClose);
    }

    protected override void OnDispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _focus.Dispose();
        _window.SizeChanged -= SizeChanged;
        base.OnDispose();
    }

    private bool Accept(DragDropEventArgs e) => Custom && !Saving && !Loading && e.Data.HasFiles && e.Data.Files.Count == 1 && (e.AllowedEffects & DragEffect.Copy) != 0;

    /// <inheritdoc/>
    public void OnDragEnter(DragDropEventArgs e) => OnDragOver(e);

    /// <inheritdoc/>
    public void OnDragOver(DragDropEventArgs e)
    {
        Dropping = Accept(e);
        e.AcceptedEffect = Dropping ? DragEffect.Copy : DragEffect.None;
    }

    /// <inheritdoc/>
    public void OnDragLeave() => Dropping = false;

    /// <inheritdoc/>
    public void OnDrop(DragDropEventArgs e)
    {
        var accept = Accept(e);
        Dropping = false;
        e.AcceptedEffect = accept ? DragEffect.Copy : DragEffect.None;
        if (accept)
            _ = ReadAsync(e.Data.Files[0]);
    }
}