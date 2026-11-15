namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Protects startup-only culture selection and token boundaries, without snapshotting every localized help string.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class LanguageSelectionTests
{
    /// <summary>
    /// A valid explicit language must bypass even an invalid environment culture before initializing command descriptions.
    /// A localized application error verifies the choice without depending on third-party parser translations.
    /// </summary>
    [TestMethod]
    [DataRow("en", "Unknown help topic")]
    [DataRow("zh-CN", "\u672a\u77e5\u7684\u5e2e\u52a9\u4e3b\u9898")]
    [DataRow("ar", "\u0645\u0648\u0636\u0648\u0639 \u0645\u0633\u0627\u0639\u062f\u0629 \u063a\u064a\u0631 \u0645\u0639\u0631\u0648\u0641")]
    [DataRow("zh-TW", "未知的說明主題")]
    [DataRow("ja", "不明なヘルプトピック")]
    [DataRow("ko", "알 수 없는 도움말 항목")]
    [DataRow("de", "Unbekanntes Hilfethema")]
    [DataRow("fr", "Sujet d’aide inconnu")]
    [DataRow("es", "Tema de ayuda desconocido")]
    [DataRow("pt-BR", "Tópico de ajuda desconhecido")]
    [DataRow("ru", "Неизвестная тема справки")]
    [DataRow("it", "Argomento della guida sconosciuto")]
    [DataRow("pl", "Nieznany temat pomocy")]
    [DataRow("tr", "Bilinmeyen yardım konusu")]
    [DataRow("id", "Topik bantuan tidak dikenal")]
    public async Task ExplicitLanguage_InvalidEnvironment_UsesSelectedCulture(string language, string expectedMessage)
    {
        using var cli = new CliSandbox();
        var start = cli.CreateStartInfo("help", "missing-topic", "--language=" + language);
        start.Environment["RODEL_CLI_LANGUAGE"] = "invalid/culture";

        var result = (await CliSandbox.RunAsync(start, cancellationToken: TestContext.CancellationToken)).ReadEnvelope(2);

        Assert.AreEqual("INVALID_ARGUMENT", result["error"]?["code"]?.GetValue<string>());
        var message = result["error"]?["message"]?.GetValue<string>();
        Assert.IsNotNull(message);
        Assert.Contains(expectedMessage, message);
        Assert.Contains("missing-topic", message);
    }

    /// <summary>
    /// An option-shaped positional value after -- must reach command handling rather than bootstrap language parsing.
    /// </summary>
    [TestMethod]
    public async Task LanguageToken_AfterSeparator_RemainsPositionalData()
    {
        using var cli = new CliSandbox();

        var result = (await cli.RunAsync("--language", "en", "help", "--", "--language")).ReadEnvelope(2);

        Assert.AreEqual("INVALID_ARGUMENT", result["error"]?["code"]?.GetValue<string>());
        Assert.AreEqual("Unknown help topic: --language.", result["error"]?["message"]?.GetValue<string>());
    }

    /// <summary>
    /// The actual child must resolve OS-style region/script tags before loading resources, and degrade unsupported or malformed
    /// environment values to English. Exact-only lookup would otherwise lose traditional Chinese and Brazilian Portuguese.
    /// </summary>
    [TestMethod]
    [DataRow("zh-Hant-HK", "未知的說明主題")]
    [DataRow("pt-PT", "Tópico de ajuda desconhecido")]
    [DataRow("de-AT", "Unbekanntes Hilfethema")]
    [DataRow("sv-SE", "Unknown help topic")]
    [DataRow("invalid/culture", "Unknown help topic")]
    public async Task EnvironmentLanguage_RegionalOrUnsupported_ResolvesBeforeCommandInitialization(string requested, string expectedMessage)
    {
        using var cli = new CliSandbox();
        var start = cli.CreateStartInfo("help", "missing-topic");
        start.Environment["RODEL_CLI_LANGUAGE"] = requested;

        var result = (await CliSandbox.RunAsync(start, cancellationToken: TestContext.CancellationToken)).ReadEnvelope(2);

        Assert.AreEqual("INVALID_ARGUMENT", result["error"]?["code"]?.GetValue<string>());
        Assert.Contains(expectedMessage, result["error"]?["message"]?.GetValue<string>() ?? string.Empty);
        Assert.Contains("missing-topic", result["error"]?["message"]?.GetValue<string>() ?? string.Empty);
    }

    /// <summary>Gets or sets the cancellation context forwarded to the actual CLI child.</summary>
    public TestContext TestContext { get; set; }
}