// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

internal sealed partial class SeedBookmarksPopup(SeedExplorerState state, Action close) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private string SaveLabel => Current.UseSavedMap ? state.Controller.Bookmarks.Saved() ? Resources.SeedUnsaveWorld : Resources.SeedSaveWorld
        : state.Controller.Bookmarks.Saved() ? Resources.SeedUnsaveSeed : Resources.SeedSaveSeed;
    private SeedBookmark[] Items => [.. Current.Bookmarks.Where(item => item.Kind == (Current.BookmarkSegment == 0 ? "seed" : "place"))];
    private Ui Body => Column([
        Column([
        Row([
            Text(Resources.SeedBookmarks).FontSize(16f).Weight(1f).VAlign(VerticalAlignment.Center),
            Button(SaveLabel,
                () => _ = state.Controller.Bookmarks.ToggleAsync()).Style(SeedChrome.Quiet).Height(32f)
                .Enabled((Current.UseSavedMap || Current.Seed.Length > 0) && Current.Catalog is not null && !Current.Opening && !Current.BookmarkBusy)
                .Visible(Current.BookmarkSegment == 0)
                .Automation(new() { Name = SaveLabel, AutomationId = "seed.save" }),
        ]).Spacing(8f),
        new SeedSegments(LauncherAppearance.Capture(),[
            string.Format(Resources.SeedWorldCountFormat, Current.Bookmarks.Count(item => item.Kind == "seed")),
            string.Format(Resources.SeedPlaceCountFormat, Current.Bookmarks.Count(item => item.Kind == "place")),
        ], Current.BookmarkSegment, index => state.Current = state.Current with { BookmarkSegment = index }, "seed.bookmarks.segment", 336f),
        Text(Resources.SeedBookmarksEmpty).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap().Visible(Items.Length == 0),
        ]).Spacing(12f).Margin(12f),
        ScrollView(new SeedBookmarkList(state, close)).Padding(new Thickness(12f, 0f, 12f, 12f))
            .MaxHeight(Math.Max(160, Math.Min(420, Current.Height - 180)) + 12f).Visible(Items.Length > 0),
    ]).Width(360f);
}

[View]
internal readonly partial record struct SeedBookmarkRow(SeedBookmark Bookmark, SeedExplorerSnapshot State, Action Open, Action Copy, Action Remove)
{
    /// <summary>Hosts one retained row/editor with stable native control identity.</summary>
    public Ui Body => new SeedBookmarkRowControl(Bookmark, State, Open, Copy, Remove);
}

internal sealed partial class SeedBookmarkRowControl : Control
{
    internal SeedBookmarkRowControl(SeedBookmark bookmark, SeedExplorerSnapshot state, Action open, Action copy, Action remove) { Bookmark = bookmark; State = state; Open = open; Copy = copy; Remove = remove; }
    private partial SeedBookmark Bookmark { get; set; }
    private partial SeedExplorerSnapshot State { get; set; }
    private partial Action Open { get; set; }
    private partial Action Copy { get; set; }
    private partial Action Remove { get; set; }
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedBookmarkRowControl other) { Bookmark = other.Bookmark; State = other.State; Open = other.Open; Copy = other.Copy; Remove = other.Remove; } }

    private string Name => Bookmark.SavedWorld is not null ? Bookmark.Name : Bookmark.Kind == "seed" ? Bookmark.Seed : Bookmark.Position is { } position ? SeedText.Position(position, State) : Bookmark.Name;
    /// <summary>Restores a record's own version/world context and keeps copy/remove separate from its primary navigation.</summary>
    public Ui Body => Border(Grid([GridLength.Star(), GridLength.Auto, GridLength.Auto], [GridLength.Auto], [
        Button(Column([
            Text(Name).FontSize(13f).Wrap(),
            Text(Bookmark.Kind == "place" ? $"X {Bookmark.X} · Z {Bookmark.Z}" : SeedText.Dimension(Bookmark.World.Dimension)).FontSize(11f).Foreground(LauncherTheme.Muted),
            Text(Bookmark.Profile.VersionId + " · " + (Bookmark.Kind == "place" ? SeedText.Dimension(Bookmark.World.Dimension) + " · " + Bookmark.Seed : SeedText.Preset(Bookmark.World.Preset))).FontSize(10f).Foreground(LauncherTheme.Muted).Wrap(),
        ]).Spacing(4f), Open).Style(SeedChrome.ListRow).Automation(new() { Name = Name, AutomationId = "seed.bookmark.open." + Bookmark.Id }).Cell(0, 0),
        new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedCopy, "copy", Copy, "seed.bookmark.copy." + Bookmark.Id, Bookmark.Kind == "place" || Bookmark.Seed.Length > 0).Cell(1, 0),
        new SeedIconCommand(LauncherAppearance.Capture(),Resources.SeedRemove, "delete", Remove, "seed.bookmark.remove." + Bookmark.Id, Critical: true).Cell(2, 0),
    ]).ColumnSpacing(4f)).BorderBrush(LauncherTheme.Edge).BorderThickness(0, 0, 0, 1).Padding(0, 6f);
}

internal sealed partial class SeedBookmarkList(SeedExplorerState state, Action close) : Control
{
    private SeedBookmark[] Items => [.. state.Current.Bookmarks.Where(item => item.Kind == (state.Current.BookmarkSegment == 0 ? "seed" : "place"))];
    private Ui Body => Column([ItemsRepeater(Items, keySelector: item => state.Controller.Bookmarks.Key(item.Id), template: item => new SeedBookmarkRow(item, state.Current,
        () => { close(); _ = state.Controller.Bookmarks.OpenAsync(item); }, () => state.Controller.Bookmarks.Copy(item), () => _ = state.Controller.Bookmarks.RemoveAsync(item.Id))).KeyboardNavigation()]);
}