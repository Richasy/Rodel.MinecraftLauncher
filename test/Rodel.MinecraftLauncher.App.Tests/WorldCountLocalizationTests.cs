// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Content;
using Sprout.Localization;
using System.Globalization;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Checks the real Windows ICU boundary and its resource selection when the displayed language changes.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WorldCountLocalizationTests
{
    /// <summary>
    /// Review: a binary singular/plural check loses Arabic's six categories and Russian's 21/22/25 forms.
    /// Existing App tests do not cross native ICU or its UTF-8 locale/UTF-16 keyword marshalling. These independent
    /// examples exercise our native binding and resource dispatch, with a different numeric culture and no ICU stub.
    /// Restoring the process language isolates this check; packaged control wiring is verified separately with UIA.
    /// </summary>
    [TestMethod]
    public void WorldCounts_FollowDisplayedLanguage_ThroughNativeCardinalCategories() => StateTestScope.Run(() =>
    {
        var language = Localizer.CurrentLanguage;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
            (string Language, int Count, string Text)[] examples =
            [
                ("en", 0, "0 worlds"),
                ("en", 1, "1 world"),
                ("en", 2, "2 worlds"),
                ("ru", 21, "21 мир"),
                ("ru", 22, "22 мира"),
                ("ru", 25, "25 миров"),
                ("pl", 1, "1 świat"),
                ("pl", 2, "2 światy"),
                ("pl", 5, "5 światów"),
                ("ar", 0, "0 عوالم"),
                ("ar", 1, "1 عالم"),
                ("ar", 2, "2 عالمان"),
                ("ar", 3, "3 عوالم"),
                ("ar", 11, "11 عالمًا"),
                ("ar", 102, "102 عالم"),
                ("zh-CN", 1, "1 个世界"),
                ("zh-CN", 2, "2 个世界"),
                ("en", 1, "1 world"),
            ];
            foreach (var (Language, Count, Text) in examples)
            {
                Localizer.CurrentLanguage = Language;
                Assert.AreEqual(Text, WorldCountText.Format(Count), $"{Language}, {Count}");
            }
        }
        finally
        {
            Localizer.CurrentLanguage = language;
            CultureInfo.CurrentCulture = culture;
        }

        return Task.CompletedTask;
    });
}