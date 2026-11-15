// Copyright (c) Richasy. All rights reserved.

using Humanizer;
using Sprout.Localization;
using System.Globalization;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Uses relative wording for the last 72 hours and a local calendar date for older projects.</summary>
internal static class SkinRecentTime
{
    private static CultureInfo Culture => CultureInfo.GetCultureInfo(Localizer.CurrentLanguage);

    internal static string Format(DateTimeOffset modified, DateTimeOffset now) => (now - modified) < TimeSpan.FromDays(3) ? modified.Humanize(dateToCompareAgainst: now, culture: Culture) : modified.ToLocalTime().ToString("d", Culture);

    internal static string Accessible(DateTimeOffset modified) => string.Format(Culture, Resources.SkinRecentModifiedAt, modified.ToLocalTime());
}