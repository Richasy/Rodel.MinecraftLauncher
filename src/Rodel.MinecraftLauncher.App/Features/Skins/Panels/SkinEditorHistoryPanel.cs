// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Displays retained transactions from the active session, including available future states.</summary>
internal sealed partial class SkinEditorHistoryPanel(SkinEditorState editor) : Control
{
    private readonly SkinHistoryList _list = new(editor);

    private Ui Body => Border(Overlay([
            _list.Visible(editor.History.Entries.Length > 1),
            new SkinEditorEmptyState(LauncherAppearance.Capture(),"history", Resources.SkinHistoryEmpty, Resources.SkinHistoryEmptyHint).Visible(editor.History.Entries.Length <= 1),
        ]));
}

internal sealed partial class SkinHistoryList(SkinEditorState editor) : Control
{
    private Ui Body => ItemsView(
        editor.History.Entries.Reverse(),
        item => item.Index,
        item => new SkinHistoryRow(LauncherAppearance.Capture(), item, item.Index > Array.FindIndex(editor.History.Entries, entry => entry.Current)),
        accessibleName: SkinHistoryList.Label,
        itemAutomation: item => new()
        {
            Name = SkinHistoryList.Label(item),
            AutomationId = "skin.history." + item.Index
        })
        .SelectedIndex(Array.FindIndex(editor.History.Entries.Reverse()
                .ToArray(), item => item.Current))
        .ItemChrome(new(SkinEditorChrome.QuietPalette, LauncherButtonPalette.Create(false, true), Sprout.Graphics.Colors.Transparent, new Sprout.Graphics.CornerRadius(0f)))
        .OnItemInvoked(Restore)
        .Enabled(editor.Controller.LayerActions.CanChange)
        .Padding(editor.SectionPadding);
    private Action<int> Restore
    {
        get
        {
            var (revision, history) = editor.History;
            var entries = history.Reverse().ToArray();
            return index =>
            {
                if ((index >= 0) && (index < entries.Length) && !entries[index].Current)
                {
                    _ = editor.Controller.RestoreToAsync(entries[index].Index, revision);
                }
            };
        }
    }

    internal static string Label(SkinHistoryEntry entry) => entry.Transaction is { Source: "ai", Label: { } label } && !string.IsNullOrWhiteSpace(label) ? label : entry.Transaction.Kind.ToLowerInvariant() switch
    {
        "create" or "template" => Resources.SkinHistoryInitial,
        "pencil" or "stroke" or "brush" => Resources.SkinHistoryStroke,
        "erase" or "eraser" => Resources.SkinHistoryErase,
        "fill" => Resources.SkinHistoryFill,
        "move" => Resources.SkinHistoryMove,
        "cut" => Resources.SkinHistoryDelete,
        "paste" => Resources.SkinHistoryPaste,
        "import" => Resources.SkinHistoryImport,
        "layer" => Resources.SkinHistoryLayer,
        _ => Resources.SkinHistoryChange,
    };
}