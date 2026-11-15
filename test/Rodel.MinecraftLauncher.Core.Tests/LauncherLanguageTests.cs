// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects regional/script resolution shared by the launcher's UI and its pre-launch game-language choice.</summary>
[TestClass]
public sealed class LauncherLanguageTests
{
    /// <summary>
    /// Exact-only or primary-subtag-only lookup would lose traditional Chinese, select the wrong Portuguese resource,
    /// or silently start the game in the old three-language fallback. These expectations come from the supported UI/game contract.
    /// </summary>
    [TestMethod]
    [DataRow("zh-Hant-HK", "zh-TW", "zh_tw")]
    [DataRow("zh-Hans-HK", "zh-CN", "zh_cn")]
    [DataRow("zh-MO", "zh-TW", "zh_tw")]
    [DataRow("zh-SG", "zh-CN", "zh_cn")]
    [DataRow("pt-PT", "pt-BR", "pt_br")]
    [DataRow("en-AU", "en", "en_us")]
    [DataRow("DE_de", "de", "de_de")]
    [DataRow("ja-JP", "ja", "ja_jp")]
    [DataRow("ko-KR", "ko", "ko_kr")]
    [DataRow("ar-SA", "ar", "ar_sa")]
    [DataRow("sv-SE", "en", "en_us")]
    [DataRow("invalid/culture", "en", "en_us")]
    [DataRow(null, "en", "en_us")]
    public void RegionalLanguage_PreservesScriptAndGameSelection(string? requested, string expectedUi, string expectedGame)
    {
        Assert.AreEqual(expectedUi, LauncherLanguages.Resolve(requested));
        Assert.AreEqual(expectedGame, LauncherLanguages.GetGameLanguage(requested));
    }
}