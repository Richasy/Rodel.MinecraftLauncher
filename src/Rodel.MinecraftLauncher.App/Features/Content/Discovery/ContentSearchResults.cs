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
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed partial class ContentSearchResults : Control
{
    private readonly ContentPage _page;
    private readonly PoolingItemFactory<ContentRowState, ContentListRow> _factory;
    private readonly ScrollView _scroll;
    private readonly Dictionary<string, int> _keys = [with(StringComparer.Ordinal)];
    private (string InstanceId, ContentKind? Kind, string Query) _context;
    private bool _restoring;
    private bool _loadQueued;
    private bool _disposed;

    internal ContentSearchResults(ContentPage page)
    {
        _page = page;
        _context = (page.Online.InstanceId, page.Online.Kind, page.Online.Query);
        _factory = new([], row => KeyFor(row.Key), row => new ContentListRow(LauncherAppearance.Capture(), row, page), autoProject: true);
        _scroll = new ScrollView(new ItemsRepeater(_factory, new StackLayout { EstimatedItemExtent = 82f }).KeyboardNavigation());
        _restoring = page.SearchOffset.Y > 0;
        _scroll.ScrollChanged += OnScroll;
    }

    private int KeyFor(string key)
    {
        if (!_keys.TryGetValue(key, out var value))
            _keys[key] = value = _keys.Count;
        return value;
    }

    private bool StandaloneNotice => !_page.QueryActive || (_page.Online.Notice is not null && _page.Online.Hits.Count == 0 && _page.Online.Local.Count == 0 && _page.Online.LocalError.Length == 0);
    private ContentSearchNotice Notice => new(!_page.QueryActive ? ContentSearchNoticeKind.Start : _page.Online.Notice ?? ContentSearchNoticeKind.Start, _page.Online.Error);
    private bool InitialLoading => _page.Online.Loading && _page.Online.Hits.Count == 0 && _page.Online.Local.Count == 0;

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Star(), GridLength.Auto],
        [
            Overlay([
                _scroll.Automation(new()
                    {
                        Name = Resources.ContentUnifiedSearch,
                        AutomationId = "content.search.results"
                    })
                    .Visible(!StandaloneNotice && !InitialLoading),
                new ContentSearchNoticeView(Notice, LauncherTheme.Ink, LauncherTheme.Muted).Visible(StandaloneNotice),
                Column([
                    ProgressBar()
                        .Indeterminate(true)
                        .Thickness(3f)
                        .Width(180f)
                        .Automation(new()
                        {
                            Name = Resources.ContentSearching,
                            AutomationId = "content.search.loading"
                        }),
                    Text(Resources.ContentSearching)
                        .FontSize(12f).Foreground(LauncherTheme.Muted)
                        .Wrap()
                        .Align(TextAlignment.Center),
                ])
                    .Spacing(12f)
                    .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                    .Visible(InitialLoading),
            ])
                .Cell(0, 0),
            ProgressBar()
                .Indeterminate(true)
                .Thickness(3f)
                .Visible(_page.Online.Loading && !InitialLoading)
                .Automation(new()
                {
                    Name = Resources.ContentSearching,
                    AutomationId = "content.search.loading-more"
                })
                .Cell(0, 1),
        ]);

    private void UpdateRows()
    {
        var context = (_page.Online.InstanceId, _page.Online.Kind, _page.Online.Query);
        if (_context != context)
        {
            _context = context;
            _restoring = false;
            _page.SearchOffset = default;
            _scroll.ScrollTo(default, animated: false);
        }

        _factory.UpdateItems(_page.SearchRows);
    }

    private void OnScroll(ScrollChangedArgs args)
    {
        if (!_restoring)
        {
            _page.SearchOffset = args.Offset;
        }

        QueueMore();
    }

    private void RestoreOffset()
    {
        // A newly measured range does not raise the offset-only ScrollChanged event.
        if (!_restoring || _scroll.MaxScrollOffset.Y <= 0f)
            return;
        _restoring = false;
        _scroll.ScrollTo(_page.SearchOffset, animated: false);
    }

    private void QueueMore()
    {
        if (_disposed || _loadQueued || !_page.Online.CanLoadMore)
            return;
        var expected = _page.Online;
        _loadQueued = true;
        _page.Window.RequestAnimationFrame(frame =>
            {
                _loadQueued = false;
                if (_disposed)
                    return;
                var current = _page.Online;
                if (current.Query != expected.Query || current.Kind != expected.Kind || current.InstanceId != expected.InstanceId || current.Offset != expected.Offset)
                    return;
                if (_scroll.ViewportSize.Height > 0 && _scroll.MaxScrollOffset.Y - _scroll.Offset.Y <= 160f)
                    _page.LoadMore();
            });
    }

    protected override IDisposable? OnMounted()
    {
        _page.SearchRowsChanged += UpdateRows;
        _page.Window.LayoutUpdated += RestoreOffset;
        UpdateRows();
        return null;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        _page.SearchRowsChanged -= UpdateRows;
        _page.Window.LayoutUpdated -= RestoreOffset;
        _scroll.ScrollChanged -= OnScroll;
        base.OnDispose();
    }
}