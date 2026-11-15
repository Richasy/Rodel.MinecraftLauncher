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

internal sealed partial class AiReferenceRow : Control
{
    private readonly Func<string, byte[]> _read;
    private bool _disposed;

    internal AiReferenceRow(
        AiChatImage image,
        Func<string, byte[]> read,
        Action<string>? remove,
        bool enabled)
    {
        _read = read;
        Update(image, remove, enabled);
    }

    private partial AiChatImage Item { get; set; }
    private partial ImageSource? Source { get; set; }
    private partial string Error { get; set; }
    private partial Action<string>? Remove { get; set; }
    private partial bool Enabled { get; set; }

    internal void Update(AiChatImage image, Action<string>? remove, bool enabled)
    {
        Item = image;
        Remove = remove;
        Enabled = enabled;
    }

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is AiReferenceRow row)
            Update(row.Item, row.Remove, row.Enabled);
    }

    private Ui Body => Border(Grid(
            [GridLength.Auto, GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                Border(When(Source is not null, new AiReferencePreview(Source!, Item.Name, Item.Id)))
                    .Width(44f).Height(44f)
                    .Cell(0, 0),
                Column([
                    Text(Item.Name)
                        .FontSize(11f).Foreground(LauncherTheme.Muted)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
                    Text(Error ?? string.Empty)
                        .FontSize(11f).Foreground(LauncherTheme.Error)
                        .Wrap()
                        .Visible(!string.IsNullOrEmpty(Error)),
                ])
                    .Spacing(4f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(1, 0),
                LauncherControls.OutlineCommand(
                    Row([
                        LauncherIcons.Line("close", 14f)
                    ]),
                    () => Remove?.Invoke(Item.Id))
                    .Style(LauncherButtons.PopupCloseButton)
                    .Width(24f).Height(24f)
                    .Enabled(Enabled)
                    .Visible(Remove is not null)
                    .ToolTip(Resources.SkinAiRemoveImage)
                    .VAlign(VerticalAlignment.Center)
                    .Automation(new()
                    {
                        Name = Resources.SkinAiRemoveImage,
                        AutomationId = "ai.reference.remove." + Item.Id
                    })
                    .Cell(2, 0),
            ])
            .ColumnSpacing(8f))
        .Padding(6f)
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(1f);

    private async Task LoadAsync()
    {
        try
        {
            var id = Item.Id;
            var bytes = await Task.Run(() => _read(id));
            if (!_disposed)
                Source = ImageSource.FromBytes(bytes);
        }
        catch (Exception error)
        {
            if (!_disposed)
                Error = error.Message;
        }
    }

    protected override IDisposable? OnMounted()
    {
        _ = LoadAsync();
        return null;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        base.OnDispose();
    }
}

[View]
internal readonly partial record struct AiReferencePreview(ImageSource Source, string Name, string Id)
{
    /// <summary>Shows the actual attached snapshot rather than a path-dependent thumbnail.</summary>
    public Ui Body => Image(Source)
        .Stretch(ImageStretch.Uniform)
        .Width(44f).Height(44f)
        .Automation(new()
        {
            Name = Name,
            AutomationId = "ai.reference." + Id
        });
}