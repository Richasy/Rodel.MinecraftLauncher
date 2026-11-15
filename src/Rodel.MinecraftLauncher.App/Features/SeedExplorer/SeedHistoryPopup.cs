// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

internal sealed partial class SeedHistoryPopup(SeedExplorerState state, Action close) : Control
{
    private SeedSearchState[] Items => [.. state.Current.Searches.Take(30)];
    private Ui Body => Column([
        Column([
            Text(Resources.SeedHistory).FontSize(16f),
            Text(Resources.SeedHistoryEmpty).FontSize(12f).Foreground(LauncherTheme.Muted).Visible(Items.Length == 0),
        ]).Spacing(12f).Margin(12f),
        ScrollView(new SeedHistoryList(state, close)).Padding(new Thickness(12f, 0f, 12f, 12f))
            .MaxHeight(Math.Max(150, Math.Min(420, state.Current.Height - 170)) + 12f).Visible(Items.Length > 0),
    ]).Width(336f);
}

[View]
internal readonly partial record struct SeedHistoryRow(LauncherAppearance Theme, SeedSearchState Search, Action Open)
{
    /// <summary>Loads retained results without silently restarting a search or changing its frozen conditions.</summary>
    public Ui Body => Border(Button(Column([
        Grid([GridLength.Star(), GridLength.Auto], [GridLength.Auto], [
            Text(Search.Query.StartSeed).FontSize(13f).Wrap().Cell(0, 0),
            Text(SeedText.Dimension(Search.Query.World.Dimension)).FontSize(10f).Foreground(Theme.Muted).VAlign(VerticalAlignment.Center).Cell(1, 0),
        ]).ColumnSpacing(8f),
        Text(string.Format(Resources.SeedSearchProgressFormat, Search.Result.Checked, Search.Result.Hits.Length, Search.Result.ElapsedMilliseconds / 1000)).FontSize(10f).Foreground(Theme.Muted).Wrap(),
        Grid([GridLength.Star(), GridLength.Auto], [GridLength.Auto], [
            Text(SeedText.Status(Search.Result.Status)).FontSize(10f).Wrap().Cell(0, 0),
            Text(Search.ModifiedAt.ToLocalTime().ToString("g")).FontSize(10f).Foreground(Theme.Quiet).VAlign(VerticalAlignment.Center).Cell(1, 0),
        ]).ColumnSpacing(8f),
    ]).Spacing(6f), Open).Style(SeedChrome.For(Theme).ListRow.Padding(12f, 10f)).Automation(new() { AutomationId = "seed.history." + Search.Id }))
        .Background(Theme.ControlBackground).BorderBrush(Theme.Edge).BorderThickness(1f);
}

internal sealed partial class SeedHistoryList(SeedExplorerState state, Action close) : Control
{
    private SeedSearchState[] Items => [.. state.Current.Searches.Take(30)];
    private Ui Body => Column([Each(Items, item => state.Controller.Bookmarks.Key("search:" + item.Id), item => new SeedHistoryRow(LauncherAppearance.Capture(),item, () =>
    { close(); _ = state.Controller.Search.ShowHistoryAsync(item); }))]).Spacing(10f);
}