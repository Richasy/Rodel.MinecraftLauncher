// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Localization;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout.Localization;
using System.Globalization;

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Formats the visible and accessible world count with the displayed language's cardinal rules.</summary>
internal static class WorldCountText
{
    internal static string Format(int count)
    {
        var language = LauncherLanguages.Resolve(Localizer.CurrentLanguage);
        var format = PluralRules.SelectCardinal(count, language) switch
        {
            "zero" => Resources.WorldCountZero,
            "one" => Resources.WorldCountOne,
            "two" => Resources.WorldCountTwo,
            "few" => Resources.WorldCountFew,
            "many" => Resources.WorldCountMany,
            "other" => Resources.WorldCountOther,
            _ => null,
        };
        // A broken system ICU must not prevent reading worlds or invent an incorrect grammatical form.
        return format is null ? count.ToString(CultureInfo.CurrentCulture) : string.Format(CultureInfo.CurrentCulture, format, count);
    }
}