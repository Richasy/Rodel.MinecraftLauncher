// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Transient interface choices shared by the titlebar and editor panes; no document state is stored here.</summary>
internal sealed class SkinEditorState
{
    private readonly Sprout.Reactive.Signal<(long Revision, Rodel.Minecraft.Skins.SkinHistoryEntry[] Entries)> _history = new((0, []));
    internal (long Revision, Rodel.Minecraft.Skins.SkinHistoryEntry[] Entries) History { get => _history.Value; set => _history.Value = value; }
    private readonly Sprout.Reactive.Signal<bool> _pasting = new(false);
    private readonly Sprout.Reactive.Signal<string> _selectionFill = new("#D6D1B9FF");
    internal bool Pasting { get => _pasting.Value; set => _pasting.Value = value; }
    internal string SelectionFill { get => _selectionFill.Value; set => _selectionFill.Value = value; }
    private readonly Sprout.Reactive.Signal<SkinEditorSelection?> _selection = new(null);
    private readonly Sprout.Reactive.Signal<bool> _selecting = new(false);
    internal SkinEditorSelection? Selection { get => _selection.Value; set => _selection.Value = value; }
    internal bool Selecting { get => _selecting.Value; set => _selecting.Value = value; }
    internal SkinEditorController Controller { get; set; } = null!;
    internal SkinPaletteController Palettes { get; set; } = null!;
    internal SkinEditorAiController Ai { get; set; } = null!;
    private readonly Sprout.Reactive.Signal<bool> _fileActive = new(false);
    internal bool FileActive { get => _fileActive.Value; set => _fileActive.Value = value; }
    private readonly Sprout.Reactive.Signal<bool> _layerEditing = new(false), _layerDragging = new(false), _layerPending = new(false);
    internal bool LayerEditing { get => _layerEditing.Value; set => _layerEditing.Value = value; }
    internal bool LayerDragging { get => _layerDragging.Value; set => _layerDragging.Value = value; }
    internal bool LayerPending { get => _layerPending.Value; set => _layerPending.Value = value; }
    internal bool HasLayerDraft => LayerEditing || LayerPending;
    internal Func<Rect?>? InspectorBounds { get; set; }
    internal Func<float, float>? ScrollInspector { get; set; }
    private readonly Sprout.Reactive.Signal<string> _layerRenameId = new(string.Empty), _layerRenameName = new(string.Empty);
    internal string LayerRenameId { get => _layerRenameId.Value; set => _layerRenameId.Value = value; }
    internal string LayerRenameName { get => _layerRenameName.Value; set => _layerRenameName.Value = value; }
    private readonly Sprout.Reactive.Signal<string> _activeLayerId = new(string.Empty);

    private sealed record LayerDisplay(Rodel.Minecraft.Skins.SkinLayerInfo[] Layers, Dictionary<string, ImageSource> Thumbnails);
    private readonly Sprout.Reactive.Signal<LayerDisplay> _layerDisplay = new(new([], []));
    internal string ActiveLayerId { get => _activeLayerId.Value; set => _activeLayerId.Value = value; }
    internal Dictionary<string, ImageSource> LayerThumbnails => _layerDisplay.Value.Thumbnails;

    internal void SetLayers(Rodel.Minecraft.Skins.SkinLayerInfo[] layers, Dictionary<string, ImageSource> thumbnails) => _layerDisplay.Value = new(layers, thumbnails);
    private readonly Sprout.Reactive.Signal<string> _projectName = new(string.Empty);
    private readonly Sprout.Reactive.Signal<string> _status = new(string.Empty);
    private readonly Sprout.Reactive.Signal<bool> _hasProject = new(false);
    private readonly Sprout.Reactive.Signal<bool> _busy = new(false);
    private readonly Sprout.Reactive.Signal<bool> _canUndo = new(false);
    private readonly Sprout.Reactive.Signal<bool> _canRedo = new(false);
    private readonly Sprout.Reactive.Signal<Rodel.Minecraft.Skins.SkinModel> _documentModel = new(default);
    private readonly Sprout.Reactive.Signal<int> _textureSize = new(64);
    internal Rodel.Minecraft.Skins.SkinModel DocumentModel { get => _documentModel.Value; set => _documentModel.Value = value; }
    internal bool IsCape => DocumentModel == Rodel.Minecraft.Skins.SkinModel.Cape;
    internal int TextureHeight => Rodel.Minecraft.Skins.SkinLayout.Height(DocumentModel, TextureSize);
    internal int TextureSize { get => _textureSize.Value; set => _textureSize.Value = value; }
    internal string ProjectName { get => _projectName.Value; set => _projectName.Value = value; }
    internal string Status { get => _status.Value; set => _status.Value = value; }
    internal bool HasProject { get => _hasProject.Value; set => _hasProject.Value = value; }
    internal bool Busy { get => _busy.Value; set => _busy.Value = value; }
    internal bool CanUndo { get => _canUndo.Value; set => _canUndo.Value = value; }
    internal bool CanRedo { get => _canRedo.Value; set => _canRedo.Value = value; }
    internal Rodel.Minecraft.Skins.SkinLayerInfo[] Layers => _layerDisplay.Value.Layers;
    private readonly Sprout.Reactive.Signal<int> _tool = new(default);
    internal int Tool
    {
        get => _tool.Value;
        set
        {
            _tool.Value = value;
            if (value == 2)
                _transparentInk.Value = false;
        }
    }
    private readonly Sprout.Reactive.Signal<int> _panel = new(default);
    internal int Panel { get => _panel.Value; set => _panel.Value = value; }
    private readonly Sprout.Reactive.Signal<bool> _mirror = new(default);
    internal bool Mirror { get => _mirror.Value; set => _mirror.Value = value; }
    internal Rodel.Minecraft.Skins.SkinPart? IsolatedLimb => IsCape ? null : Parts switch
    {
        4 => Rodel.Minecraft.Skins.SkinPart.RightArm,
        8 => Rodel.Minecraft.Skins.SkinPart.LeftArm,
        16 => Rodel.Minecraft.Skins.SkinPart.RightLeg,
        32 => Rodel.Minecraft.Skins.SkinPart.LeftLeg,
        _ => null,
    };
    internal Rodel.Minecraft.Skins.SkinMirrorMode MirrorMode => Mirror
        ? Rodel.Minecraft.Skins.SkinMirrorMode.Body : Rodel.Minecraft.Skins.SkinMirrorMode.None;
    internal Rodel.Minecraft.Skins.SkinPart[]? MirrorParts
    {
        get
        {
            if (!Mirror)
                return null;
            var parts = Parts;
            return [.. Enum.GetValues<Rodel.Minecraft.Skins.SkinPart>().Where(part => (parts & (1 << (int)part)) != 0)];
        }
    }
    internal bool ShowCopyToOpposite => Mirror && IsolatedLimb is not null;
    private readonly Sprout.Reactive.Signal<bool> _flatView = new(default);
    internal bool FlatView { get => _flatView.Value; set => _flatView.Value = value; }
    private readonly Sprout.Reactive.Signal<bool> _gridVisible = new(default);
    internal bool GridVisible { get => _gridVisible.Value; set => _gridVisible.Value = value; }
    private readonly Sprout.Reactive.Signal<bool> _uvVisible = new(true);
    internal bool UvVisible { get => _uvVisible.Value; set => _uvVisible.Value = value; }
    internal bool ShowUv => UvVisible && !FlatView;
    private readonly Sprout.Reactive.Signal<bool> _outerSurface = new(default);
    internal bool OuterSurface { get => _outerSurface.Value; set => _outerSurface.Value = value; }
    private readonly Sprout.Reactive.Signal<bool> _outerVisible = new(true);
    internal bool OuterVisible { get => _outerVisible.Value; set => _outerVisible.Value = value; }
    private readonly Sprout.Reactive.Signal<int> _parts = new(63);
    internal int Parts { get => _parts.Value; set => _parts.Value = value & 127; }

    internal bool IsPartVisible(int part) => (Parts & (1 << part)) != 0;

    internal void TogglePart(int part) => Parts ^= 1 << part;
    private readonly Sprout.Reactive.Signal<double> _zoom = new(100);
    internal double Zoom { get => _zoom.Value; set => _zoom.Value = value; }
    internal Action<double>? SetZoom { get; set; }
    internal double MinimumZoom => FlatView ? 25 : 6000d / 110;
    internal double MaximumZoom => FlatView ? 3200 : 6000d / 28;
    private readonly Sprout.Reactive.Signal<double> _brushSize = new(1);
    internal double BrushSize { get => _brushSize.Value; set => _brushSize.Value = value; }
    private readonly Sprout.Reactive.Signal<string> _foreground = new("#638B4DFF");
    private readonly Sprout.Reactive.Signal<bool> _transparentInk = new(false);
    internal bool TransparentInk => _transparentInk.Value;

    internal void ToggleTransparentInk()
    {
        _transparentInk.Value = !TransparentInk;
        if (TransparentInk)
            Tool = 0;
    }

    private readonly Sprout.Reactive.Signal<Color> _lastValidColor = new(new(99, 139, 77, 255));
    internal string Foreground
    {
        get => _foreground.Value;
        set
        {
            _transparentInk.Value = false;
            _foreground.Value = value;
            if (TryParseColor(value, out var color))
                _lastValidColor.Value = color;
        }
    }
    private readonly Sprout.Reactive.Signal<float> _width = new(1280f);
    private readonly Sprout.Reactive.Signal<float> _height = new(800f);
    internal float Width { get => _width.Value; set => _width.Value = value; }
    internal float Height { get => _height.Value; set => _height.Value = value; }
    internal bool Compact => Width < 1100f;
    internal float RailWidth => Compact ? 54f : 62f;
    private readonly Sprout.Reactive.Signal<float> _inspectorWidth = new(320f);
    internal float InspectorMaximum => Math.Max(240f, Math.Min(640f, Width - RailWidth - 320f));
    internal float InspectorWidth { get => Math.Clamp(_inspectorWidth.Value, 240f, InspectorMaximum); set => _inspectorWidth.Value = value; }
    internal float SectionPadding => Compact ? 14f : 18f;
    internal float ToolHeight => Height < 760f ? 40f : 44f;
    internal float UvWidth => Compact ? 170f : 220f;
    internal float UvHeight => Compact ? 205f : 255f;
    internal float UvBottom => Height < 760f ? 40f : 90f;
    internal Func<Rect?>? UvBounds { get; set; }

    internal bool IsOverUv(Point point) => ShowUv && UvBounds?.Invoke() is { } bounds && (point.X >= bounds.Left) && (point.X < bounds.Right) && (point.Y >= bounds.Top) && (point.Y < bounds.Bottom);

    internal void SetColor(Color color) => Foreground = $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
    internal string ToolName => Tool switch
    {
        1 => Resources.SkinEraser,
        2 => Resources.SkinFill,
        3 => Resources.SkinPicker,
        4 => Resources.SkinSelection,
        0 when TransparentInk => Resources.SkinTransparentColor,
        _ => Resources.SkinBrush,
    };
    internal Color ForegroundColor => _lastValidColor.Value;
    internal bool ValidColor => TryParseColor(Foreground, out _);

    internal static bool TryParseColor(string text, out Color color)
    {
        color = default;
        if ((text.Length != 9) || (text[0] != '#') || !uint.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgba))
        {
            return false;
        }

        color = new((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        return true;
    }
}