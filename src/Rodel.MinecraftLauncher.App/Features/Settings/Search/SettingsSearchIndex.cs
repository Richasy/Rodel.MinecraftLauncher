// Copyright (c) Richasy. All rights reserved.

using System.Globalization;
using System.Text;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed record SettingsSearchEntry(
    string Id,
    string Category,
    string Title,
    string Description)
{
    internal string CategoryTitle => SettingsSearchIndex.CategoryTitle(Category);
}

/// <summary>Searches visible resource text on each read; adding a language needs no language-specific search table.</summary>
internal static class SettingsSearchIndex
{
    internal static string CategoryTitle(string category) => category switch
    {
        "launch" => Resources.SettingsGameLaunch,
        "java" => Resources.SettingsJavaMemory,
        "network" => Resources.SettingsNetwork,
        "content" => Resources.SettingsContentCache,
        "logs" => Resources.SettingsLogs,
        "ai" => Resources.SettingsAssistant,
        "advanced" => Resources.SettingsAdvancedNavigation,
        "about" => Resources.SettingsAbout,
        _ => Resources.SettingsLauncher,
    };
    internal static SettingsSearchEntry[] Entries =>
    [
        .. CorePreferenceFields.All.Select(preference => new SettingsSearchEntry(
            "settings." + (preference.Kind == PreferenceKind.Integer
                ? "number."
                : preference.Kind is PreferenceKind.Boolean or PreferenceKind.MemoryMode or PreferenceKind.Proxy or PreferenceKind.DownloadSource ? "field." : "input.") + preference.Key,
            preference.Category,
            preference.Title(),
            preference.Description?.Invoke() ?? string.Empty)),
        new("settings.input.network.proxy", "network", Resources.SettingsProxyAddress, Resources.SettingsProxyAddressHelp),
        new("settings.ai.services", "ai", Resources.AiServices, Resources.AiAddService),
        new("settings.ai.default-mode", "ai", Resources.AiDefaultMode, Resources.AiDefaultModeHelp + " " + Resources.AiAllowAllMode + " " + Resources.AiSafeMode),
        new("settings.ai.capabilities", "ai", Resources.AiCapabilities, Resources.AiFolders),
        new("settings.ai.integrations", "ai", Resources.AiExternal, "Codex Claude Code GitHub Copilot CLI DeepSeek Harness"),
        new("settings.language", "general", Resources.LanguageHeading, Resources.SettingsSearchLanguageKeywords),
        new("settings.theme", "general", Resources.ThemeHeading, Resources.SettingsSearchThemeKeywords),
        new("wallpaper.light.choose", "general", Resources.WallpaperHeading, Resources.SettingsSearchWallpaperKeywords),
        new("settings.multiplayer.enabled", "general", Resources.MultiplayerEnabled, Resources.MultiplayerSettingsDescription),
        new("settings.multiplayer-mini", "general", Resources.MultiplayerMiniWindow, string.Empty),
        new("settings.after-launch", "general", Resources.AfterLaunchHeading, Resources.AfterLaunchTray + " " + Resources.AfterLaunchTaskbar + " " + Resources.AfterLaunchKeepVisible),
        new("settings.close-behavior", "general", Resources.CloseBehaviorHeading, Resources.CloseBehaviorHelp),
        new("settings.data.choose", "general", Resources.DataDirectoryHeading, Resources.DataDirectoryHelp),
        new("settings.java.manager", "java", Resources.JavaManager, Resources.SettingsSearchJavaKeywords),
        new("settings.cleanup.open", "content", Resources.CacheCleanupTitle, Resources.CacheCleanupDescription),
        new("settings.cache.clear", "content", Resources.ImageCacheHeading, Resources.ImageCacheDescription),
        new("settings.diagnostics.open", "logs", Resources.LauncherLogsTitle, Resources.LauncherLogsHelp),
        new("settings.game-logs.open", "logs", Resources.GameLogsTitle, Resources.GameLogsHelp),
        new("settings.about.copy-version", "about", Resources.SettingsAbout, Resources.AboutCopyVersion),
        new("settings.about.developer-apps", "about", Resources.AboutDeveloperApps, AboutSettingsSection.Publisher + " Microsoft Store"),
        new("settings.about.repository", "about", Resources.AboutRepository, Resources.AboutRepositoryDescription),
        new("settings.about.feedback", "about", Resources.AboutFeedback, Resources.AboutFeedbackDescription),
        new("settings.about.credit.sprout.project", "about", Resources.AboutCredits, Resources.AboutCreditsDescription),
        new("settings.about.privacy", "about", Resources.AboutPrivacy, string.Empty),
        new("settings.about.terms", "about", Resources.AboutTerms, string.Empty),
    ];

    internal static SettingsSearchEntry[] Search(string query)
    {
        var terms = query.Normalize(NormalizationForm.FormKC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0)
            return [];
        var comparison = CultureInfo.CurrentUICulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;
        bool Contains(string text, string term) => comparison.IndexOf(text.Normalize(NormalizationForm.FormKC), term, options) >= 0;
        var full = string.Join(" ", terms);
        return [..Entries.Where(entry => terms.All(term => Contains(entry.Title, term) || Contains(entry.Description, term) || Contains(entry.CategoryTitle, term))).OrderBy(entry => comparison.Compare(entry.Title, full, options) == 0
                    ? 0
                    : comparison.IsPrefix(entry.Title, full, options)
                    ? 1
                    : Contains(entry.Title, full) ? 2 : 3).ThenBy(entry => entry.CategoryTitle, StringComparer.CurrentCulture).ThenBy(entry => entry.Title, StringComparer.CurrentCulture).Take(15)];
    }
}