// Copyright (c) Richasy. All rights reserved.

using System.Globalization;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal enum ContentSortColumn
{
    Name,
    Version,
    Status
}

internal static class ContentSorting
{
    internal static IEnumerable<ContentItem> Order(IEnumerable<ContentItem> items, ContentSortColumn column, bool descending)
    {
        var text = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);
        var sorted = column switch
        {
            ContentSortColumn.Version => descending ? items.OrderBy(item => item.Version.Length == 0).ThenByDescending(item => item.Version, text) : items.OrderBy(item => item.Version.Length == 0).ThenBy(item => item.Version, text),
            ContentSortColumn.Status => descending ? items.OrderBy(item => item.InUse ?? item.Enabled) : items.OrderByDescending(item => item.InUse ?? item.Enabled),
            _ => descending ? items.OrderByDescending(item => item.Name, text) : items.OrderBy(item => item.Name, text),
        };
        return sorted.ThenBy(item => item.Name, text).ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase);
    }
}