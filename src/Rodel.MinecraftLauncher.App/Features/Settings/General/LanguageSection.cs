// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Graphics;
using Sprout.Localization;
using System.Globalization;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

/// <summary>Owns the application's persisted language and content direction.</summary>
internal sealed partial class LanguageSection(Window window, AppSettings settings) : Control
{
    internal static string GameLanguage => LauncherLanguages.GetGameLanguage(Localizer.CurrentLanguage);

    internal static string Resolve(string language) => LauncherLanguages.Resolve(language);

    internal static void Apply(string preference, Window? target = null)
    {
        var language = Resolve(string.IsNullOrWhiteSpace(preference) ? CultureInfo.CurrentUICulture.Name : preference);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture;
        target?.FlowDirection = language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Localizer.CurrentLanguage = language;
        target?.Title = Resources.AppTitle;
    }

    /// <summary>Gets whether the last language preference could not be persisted.</summary>
    public partial bool SaveFailed { get; private set; }
    /// <summary>Gets the settings width available after the navigation area.</summary>
    public partial float AvailableWidth { get; private set; }

    private void Resize(object? sender, WindowSizeChangedEventArgs args) => AvailableWidth = args.Size.Width - 188f;

    protected override IDisposable? OnMounted()
    {
        AvailableWidth = window.ClientSize.Width - 188f;
        window.SizeChanged += Resize;
        return null;
    }

    protected override void OnDispose()
    {
        window.SizeChanged -= Resize;
        base.OnDispose();
    }

    private Ui Body => Column([
        new SettingsChoiceRow(
LauncherAppearance.Capture(),            Resources.LanguageHeading,
            [Resources.LanguageChinese, Resources.LanguageChineseTraditional, Resources.LanguageEnglish, Resources.LanguageJapanese, Resources.LanguageKorean, Resources.LanguageGerman, Resources.LanguageFrench, Resources.LanguageSpanish, Resources.LanguagePortuguese, Resources.LanguageRussian, Resources.LanguageItalian, Resources.LanguagePolish, Resources.LanguageTurkish, Resources.LanguageIndonesian, Resources.LanguageArabic],
            LauncherLanguages.Supported.ToList()
                .IndexOf(Localizer.CurrentLanguage),
            index => Choose(index),
            AvailableWidth,
            LauncherTheme.Ink,
            LauncherTheme.Muted,
            LauncherMenus.Choice,
            LauncherMenus.ChoiceFace,
            LauncherMenus.ChoiceMenu,
            "settings.language",
            Resources.SettingsLanguageHelp),
        Text(Resources.LanguageSaveError)
            .Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(SaveFailed),
    ])
        .Spacing(8f);

    private void Choose(int index)
    {
        if (index < 0 || index >= LauncherLanguages.Supported.Count)
            return;
        try
        {
            settings.Language = LauncherLanguages.Supported[index];
            Apply(settings.Language, window);
            SaveFailed = false;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            SaveFailed = true;
        }
    }
}