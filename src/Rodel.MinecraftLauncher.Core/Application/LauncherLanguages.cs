// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Defines the launcher UI languages shared by CLI and App preferences, including their Minecraft equivalents.</summary>
public static class LauncherLanguages
{
    /// <summary>Gets the fifteen canonical language tags accepted by explicit launcher language selection.</summary>
    public static IReadOnlyList<string> Supported { get; } = Array.AsReadOnly<string>(
        ["zh-CN", "zh-TW", "en", "ja", "ko", "de", "fr", "es", "pt-BR", "ru", "it", "pl", "tr", "id", "ar"]);

    /// <summary>Resolves an OS or saved language to a shipped translation, preserving Chinese script and using English for unsupported languages.</summary>
    /// <param name="language">A culture tag; an absent or unrecognized tag uses English.</param>
    /// <returns>The canonical supported tag. Portuguese regions share Brazilian Portuguese; traditional Chinese regions share the Taiwan translation.</returns>
    public static string Resolve(string? language)
    {
        var parts = language?.Trim().Replace('_', '-').ToLowerInvariant().Split('-') ?? [];
        if (parts.Length == 0)
            return "en";
        if (parts[0] == "zh")
        {
            if (parts.Contains("hans"))
                return "zh-CN";
            return parts.Contains("hant") || parts.Contains("tw") || parts.Contains("hk") || parts.Contains("mo") ? "zh-TW" : "zh-CN";
        }

        if (parts[0] == "pt")
            return "pt-BR";
        return Supported.FirstOrDefault(value => string.Equals(value, parts[0], StringComparison.Ordinal)) ?? "en";
    }

    /// <summary>Maps a launcher language to Minecraft's resource language code without changing an existing game process.</summary>
    /// <param name="language">A supported tag or an OS culture resolved by <see cref="Resolve"/>.</param>
    /// <returns>The language code passed to the existing Core launch preparation.</returns>
    public static string GetGameLanguage(string? language) => Resolve(language) switch
    {
        "zh-CN" => "zh_cn",
        "zh-TW" => "zh_tw",
        "ja" => "ja_jp",
        "ko" => "ko_kr",
        "de" => "de_de",
        "fr" => "fr_fr",
        "es" => "es_es",
        "pt-BR" => "pt_br",
        "ru" => "ru_ru",
        "it" => "it_it",
        "pl" => "pl_pl",
        "tr" => "tr_tr",
        "id" => "id_id",
        "ar" => "ar_sa",
        _ => "en_us",
    };
}